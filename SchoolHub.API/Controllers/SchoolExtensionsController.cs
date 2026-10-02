using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using SchoolHub.API.Security;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Security.Claims;

namespace SchoolHub.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SchoolExtensionsController : ControllerBase
    {
        private readonly string _connectionString;

        public SchoolExtensionsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        // --- ACADEMIC YEARS ---
        [HttpGet("academic-years")]
        public async Task<IActionResult> GetAcademicYears()
        {
            using var db = Connection;
            return Ok(await db.QueryAsync("SELECT * FROM AcademicYears"));
        }

        [HttpPost("academic-years")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateAcademicYear([FromBody] CreateAcademicYearDto dto)
        {
            using var db = Connection;
            var sql = "INSERT INTO AcademicYears (Name, StartDate, EndDate, IsCurrent) VALUES (@Name, @StartDate, @EndDate, @IsCurrent) RETURNING Id;";
            var id = await db.ExecuteScalarAsync<int>(sql, dto);
            return Ok(new { Message = "Academic year created successfully", Id = id });
        }

        // --- TIME SLOTS (the bell schedule the timetable slots into) ---
        [HttpGet("timeslots")]
        public async Task<IActionResult> GetTimeSlots()
        {
            using var db = Connection;
            var rows = await db.QueryAsync(
                "SELECT Id, StartTime, EndTime, Label FROM TimeSlots ORDER BY StartTime, Id");
            return Ok(rows);
        }

        [HttpPost("timeslots")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateTimeSlot([FromBody] TimeSlotDto dto)
        {
            using var db = Connection;
            var error = TryReadSlot(dto, out var start, out var end, out var label);
            if (error != null) return BadRequest(new { Message = error });

            if (await SlotOverlapsAsync(db, start, end, null))
                return Conflict(new { Message = "This period overlaps an existing one." });

            var id = await db.ExecuteScalarAsync<int>(
                "INSERT INTO TimeSlots (StartTime, EndTime, Label) VALUES (@Start::time, @End::time, @Label) RETURNING Id",
                new { Start = start.ToString("HH:mm:ss"), End = end.ToString("HH:mm:ss"), Label = label });
            return Ok(new { Message = "Period created successfully", TimeSlotId = id });
        }

        [HttpPut("timeslots/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateTimeSlot(int id, [FromBody] TimeSlotDto dto)
        {
            using var db = Connection;
            var exists = await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM TimeSlots WHERE Id = @Id", new { Id = id });
            if (!exists.HasValue) return NotFound(new { Message = "Period not found." });

            var error = TryReadSlot(dto, out var start, out var end, out var label);
            if (error != null) return BadRequest(new { Message = error });

            if (await SlotOverlapsAsync(db, start, end, id))
                return Conflict(new { Message = "This period overlaps an existing one." });

            await db.ExecuteAsync(
                "UPDATE TimeSlots SET StartTime = @Start::time, EndTime = @End::time, Label = @Label WHERE Id = @Id",
                new { Id = id, Start = start.ToString("HH:mm:ss"), End = end.ToString("HH:mm:ss"), Label = label });
            return Ok(new { Message = "Period updated successfully" });
        }

        [HttpDelete("timeslots/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteTimeSlot(int id)
        {
            using var db = Connection;
            var exists = await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM TimeSlots WHERE Id = @Id", new { Id = id });
            if (!exists.HasValue) return NotFound(new { Message = "Period not found." });

            // The FK cascades, so deleting a period would silently wipe the rows
            // scheduled into it. Refuse and let the admin move them first.
            var used = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM TimetableEntries WHERE TimeSlotId = @Id", new { Id = id });
            if (used > 0)
                return Conflict(new
                {
                    Message = $"This period is used by {used} timetable " +
                              $"entr{(used == 1 ? "y" : "ies")}. Move or remove them first.",
                });

            await db.ExecuteAsync("DELETE FROM TimeSlots WHERE Id = @Id", new { Id = id });
            return Ok(new { Message = "Period deleted successfully" });
        }

        // --- TIMETABLE ---
        // One projection shared by every read, so the admin grid, a learner's own
        // week and the clash checks all agree on what an entry is.
        private const string TimetableSelect = @"
            SELECT t.Id, t.ClassId, c.Name AS ClassName,
                   t.SectionId, sec.Name AS SectionName,
                   t.SubjectId, sub.Name AS SubjectName,
                   t.TeacherId, u.Username AS TeacherName,
                   t.TimeSlotId, ts.StartTime, ts.EndTime, ts.Label AS SlotLabel,
                   t.DayOfWeek
            FROM TimetableEntries t
            JOIN Classes c ON c.Id = t.ClassId
            JOIN Sections sec ON sec.Id = t.SectionId
            JOIN Subjects sub ON sub.Id = t.SubjectId
            JOIN TimeSlots ts ON ts.Id = t.TimeSlotId
            LEFT JOIN Teachers th ON th.Id = t.TeacherId
            LEFT JOIN Users u ON u.Id = th.UserId";

        [HttpGet("timetable")]
        public async Task<IActionResult> GetTimetable([FromQuery] int classId, [FromQuery] int sectionId)
        {
            if (classId <= 0 || sectionId <= 0)
                return BadRequest(new { Message = "A class and section are required." });

            using var db = Connection;

            // Section ids are unique across classes, but a section picked for the
            // wrong class would silently show another class's week.
            var belongs = await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM Sections WHERE Id = @SectionId AND ClassId = @ClassId",
                new { SectionId = sectionId, ClassId = classId });
            if (!belongs.HasValue)
                return BadRequest(new { Message = "That section does not belong to the selected class." });

            var rows = await db.QueryAsync(
                TimetableSelect +
                " WHERE t.ClassId = @ClassId AND t.SectionId = @SectionId" +
                " ORDER BY t.DayOfWeek, ts.StartTime",
                new { ClassId = classId, SectionId = sectionId });
            return Ok(rows);
        }

        /// <summary>
        /// The caller's own week. A Student sees their class's timetable and a
        /// Teacher everything they are scheduled to teach; the class is resolved
        /// server-side, so a learner cannot ask for another section's.
        /// </summary>
        [HttpGet("timetable/mine")]
        public async Task<IActionResult> GetMyTimetable()
        {
            using var db = Connection;
            var userId = StudentAccess.CallerUserId(User);

            if (User.IsInRole("Teacher"))
            {
                var teacherId = await db.ExecuteScalarAsync<int?>(
                    "SELECT Id FROM Teachers WHERE UserId = @UserId", new { UserId = userId });
                if (!teacherId.HasValue)
                    return NotFound(new { Message = "This account is not linked to a teacher record." });

                var taught = await db.QueryAsync(
                    TimetableSelect +
                    " WHERE t.TeacherId = @TeacherId ORDER BY t.DayOfWeek, ts.StartTime",
                    new { TeacherId = teacherId.Value });
                return Ok(taught);
            }

            var studentId = await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM Students WHERE UserId = @UserId", new { UserId = userId });
            if (!studentId.HasValue)
                return NotFound(new { Message = "This account is not linked to a student record." });

            return Ok(await QueryStudentTimetableAsync(db, studentId.Value));
        }

        /// <summary>
        /// One student's week. Admin and Teacher may read any student; a Student
        /// only their own and a Parent only a linked child, so changing the id in
        /// the URL is not enough to read another learner's schedule.
        /// </summary>
        [HttpGet("timetable/student/{studentId}")]
        public async Task<IActionResult> GetStudentTimetable(int studentId)
        {
            using var db = Connection;
            if (!await StudentAccess.CanReadStudentAsync(db, User, studentId)) return Forbid();
            return Ok(await QueryStudentTimetableAsync(db, studentId));
        }

        private static async Task<IEnumerable<dynamic>> QueryStudentTimetableAsync(
            IDbConnection db, int studentId)
        {
            return await db.QueryAsync(
                TimetableSelect + @"
                JOIN Enrollments e ON e.ClassId = t.ClassId AND e.SectionId = t.SectionId
                WHERE e.StudentId = @StudentId
                ORDER BY t.DayOfWeek, ts.StartTime",
                new { StudentId = studentId });
        }

        [HttpPost("timetable")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateTimetableEntry([FromBody] CreateTimetableDto dto)
        {
            using var db = Connection;
            var (error, status) = await ValidateEntryAsync(db, dto, null);
            if (error != null) return StatusCode(status, new { Message = error });

            var id = await db.ExecuteScalarAsync<int>(
                @"INSERT INTO TimetableEntries (ClassId, SectionId, SubjectId, TeacherId, TimeSlotId, DayOfWeek)
                  VALUES (@ClassId, @SectionId, @SubjectId, @TeacherId, @TimeSlotId, @DayOfWeek)
                  RETURNING Id",
                EntryParams(dto));
            return Ok(new { Message = "Timetable entry created successfully", Id = id });
        }

        [HttpPut("timetable/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateTimetableEntry(int id, [FromBody] CreateTimetableDto dto)
        {
            using var db = Connection;
            var exists = await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM TimetableEntries WHERE Id = @Id", new { Id = id });
            if (!exists.HasValue) return NotFound(new { Message = "Timetable entry not found." });

            // Exclude this row from both clash checks, so re-saving an unchanged
            // entry does not collide with itself.
            var (error, status) = await ValidateEntryAsync(db, dto, id);
            if (error != null) return StatusCode(status, new { Message = error });

            await db.ExecuteAsync(
                @"UPDATE TimetableEntries
                  SET ClassId = @ClassId, SectionId = @SectionId, SubjectId = @SubjectId,
                      TeacherId = @TeacherId, TimeSlotId = @TimeSlotId, DayOfWeek = @DayOfWeek
                  WHERE Id = @Id",
                new
                {
                    Id = id,
                    dto.ClassId,
                    dto.SectionId,
                    dto.SubjectId,
                    TeacherId = dto.TeacherId > 0 ? dto.TeacherId : (int?)null,
                    dto.TimeSlotId,
                    dto.DayOfWeek,
                });
            return Ok(new { Message = "Timetable entry updated successfully" });
        }

        [HttpDelete("timetable/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteTimetableEntry(int id)
        {
            using var db = Connection;
            var affected = await db.ExecuteAsync(
                "DELETE FROM TimetableEntries WHERE Id = @Id", new { Id = id });
            if (affected == 0) return NotFound(new { Message = "Timetable entry not found." });
            return Ok(new { Message = "Timetable entry deleted successfully" });
        }

        /// <summary>Normalises the optional teacher to a nullable id for the SQL.</summary>
        private static object EntryParams(CreateTimetableDto dto) => new
        {
            dto.ClassId,
            dto.SectionId,
            dto.SubjectId,
            TeacherId = dto.TeacherId > 0 ? dto.TeacherId : (int?)null,
            dto.TimeSlotId,
            dto.DayOfWeek,
        };

        /// <summary>
        /// Validates an entry and returns the failure message with the status it
        /// deserves: 400 for bad input, 409 for a clash with an existing entry.
        /// </summary>
        private static async Task<(string? Error, int Status)> ValidateEntryAsync(
            IDbConnection db, CreateTimetableDto dto, int? excludeId)
        {
            if (dto.DayOfWeek < 1 || dto.DayOfWeek > 7)
                return ("Day of week must be between 1 (Monday) and 7 (Sunday).", 400);

            var sectionBelongs = await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM Sections WHERE Id = @SectionId AND ClassId = @ClassId",
                new { dto.SectionId, dto.ClassId });
            if (!sectionBelongs.HasValue)
                return ("The section does not belong to the selected class.", 400);

            var subjectInClass = await db.ExecuteScalarAsync<int?>(
                "SELECT SubjectId FROM ClassSubjects WHERE ClassId = @ClassId AND SubjectId = @SubjectId",
                new { dto.ClassId, dto.SubjectId });
            if (!subjectInClass.HasValue)
                return ("That subject is not offered in the selected class.", 400);

            var slotExists = await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM TimeSlots WHERE Id = @TimeSlotId", new { dto.TimeSlotId });
            if (!slotExists.HasValue)
                return ("The selected period does not exist.", 400);

            if (dto.TeacherId > 0)
            {
                var teacherExists = await db.ExecuteScalarAsync<int?>(
                    "SELECT Id FROM Teachers WHERE Id = @TeacherId", new { dto.TeacherId });
                if (!teacherExists.HasValue)
                    return ("The selected teacher does not exist.", 400);
            }

            var sectionClash = await db.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM TimetableEntries
                  WHERE ClassId = @ClassId AND SectionId = @SectionId AND TimeSlotId = @TimeSlotId
                    AND DayOfWeek = @DayOfWeek AND Id <> @ExcludeId",
                new { dto.ClassId, dto.SectionId, dto.TimeSlotId, dto.DayOfWeek, ExcludeId = excludeId ?? 0 });
            if (sectionClash > 0)
                return ("This section already has a subject in that period.", 409);

            if (dto.TeacherId > 0)
            {
                var teacherClash = await db.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM TimetableEntries
                      WHERE TeacherId = @TeacherId AND TimeSlotId = @TimeSlotId AND DayOfWeek = @DayOfWeek
                        AND Id <> @ExcludeId",
                    new { dto.TeacherId, dto.TimeSlotId, dto.DayOfWeek, ExcludeId = excludeId ?? 0 });
                if (teacherClash > 0)
                    return ("This teacher is already scheduled in that period.", 409);
            }

            return (null, 200);
        }

        /// <summary>
        /// Reads a period's times, rejecting anything that is not a real time or
        /// that ends before it starts. Returns a message, or null when valid.
        /// </summary>
        private static string? TryReadSlot(
            TimeSlotDto dto, out TimeOnly start, out TimeOnly end, out string? label)
        {
            label = string.IsNullOrWhiteSpace(dto.Label) ? null : dto.Label.Trim();

            if (!TimeOnly.TryParse(dto.StartTime, CultureInfo.InvariantCulture, out start))
            {
                end = default;
                return "Start time is required (HH:mm).";
            }
            if (!TimeOnly.TryParse(dto.EndTime, CultureInfo.InvariantCulture, out end))
                return "End time is required (HH:mm).";
            if (start >= end)
                return "The period must end after it starts.";
            return null;
        }

        private static async Task<bool> SlotOverlapsAsync(
            IDbConnection db, TimeOnly start, TimeOnly end, int? excludeId)
        {
            var count = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM TimeSlots WHERE StartTime < @End::time AND EndTime > @Start::time AND Id <> @ExcludeId",
                new { Start = start.ToString("HH:mm:ss"), End = end.ToString("HH:mm:ss"), ExcludeId = excludeId ?? 0 });
            return count > 0;
        }

        // --- EVENTS ---
        // An event is created and owned by staff, and everyone it concerns says
        // whether they are coming. Reads are open to any signed-in user because
        // the list carries the caller's own response (MyStatus) and the response
        // count; only staff may change the event, and only the caller (or staff)
        // may remove a response.
        private static readonly string[] EventStatuses =
            { "Invited", "Attending", "Not Attending", "Maybe" };

        private static string? ValidateEvent(SaveEventDto dto, out string title, out string? location)
        {
            title = (dto.Title ?? string.Empty).Trim();
            location = string.IsNullOrWhiteSpace(dto.Location) ? null : dto.Location.Trim();

            if (title.Length == 0)
                return "Event title is required.";
            if (title.Length > 255)
                return "Event title must be 255 characters or fewer.";
            if (location is { Length: > 255 })
                return "Location must be 255 characters or fewer.";
            if (dto.EventDate == default)
                return "Event date is required.";

            return null;
        }

        private const string EventSelect =
            @"SELECT e.Id, e.Title, e.Description, e.EventDate, e.Location,
                     (SELECT COUNT(*) FROM EventParticipants p WHERE p.EventId = e.Id) AS ParticipantCount,
                     (SELECT p.Status FROM EventParticipants p
                       WHERE p.EventId = e.Id AND p.UserId = @UserId) AS MyStatus
              FROM Events e";

        [HttpGet("events")]
        public async Task<IActionResult> GetEvents()
        {
            using var db = Connection;
            var rows = await db.QueryAsync(
                EventSelect + " ORDER BY e.EventDate ASC",
                new { UserId = StudentAccess.CallerUserId(User) });
            return Ok(rows);
        }

        [HttpGet("events/{id}")]
        public async Task<IActionResult> GetEvent(int id)
        {
            using var db = Connection;
            var row = await db.QuerySingleOrDefaultAsync(
                EventSelect + " WHERE e.Id = @Id",
                new { Id = id, UserId = StudentAccess.CallerUserId(User) });
            if (row == null) return NotFound(new { Message = "Event not found." });
            return Ok(row);
        }

        [HttpGet("events/{id}/participants")]
        public async Task<IActionResult> GetEventParticipants(int id)
        {
            using var db = Connection;
            var exists = await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM Events WHERE Id = @Id", new { Id = id });
            if (!exists.HasValue) return NotFound(new { Message = "Event not found." });

            var rows = await db.QueryAsync(
                @"SELECT u.Id AS UserId, u.Username, p.Status
                  FROM EventParticipants p
                  JOIN Users u ON u.Id = p.UserId
                  WHERE p.EventId = @Id
                  ORDER BY u.Username",
                new { Id = id });
            return Ok(rows);
        }

        [HttpPost("events")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateEvent([FromBody] SaveEventDto dto)
        {
            using var db = Connection;
            var error = ValidateEvent(dto, out var title, out var location);
            if (error != null) return BadRequest(new { Message = error });

            var duplicate = await db.ExecuteScalarAsync<int?>(
                @"SELECT Id FROM Events
                  WHERE lower(btrim(Title)) = lower(@Title) AND EventDate = @EventDate",
                new { Title = title, dto.EventDate });
            if (duplicate.HasValue)
                return Conflict(new { Message = "An event with that title already exists on that date." });

            var id = await db.ExecuteScalarAsync<int>(
                @"INSERT INTO Events (Title, Description, EventDate, Location)
                  VALUES (@Title, @Description, @EventDate, @Location)
                  RETURNING Id",
                new { Title = title, Description = dto.Description, dto.EventDate, Location = location });
            return Ok(new { Message = "Event created successfully", Id = id });
        }

        [HttpPut("events/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateEvent(int id, [FromBody] SaveEventDto dto)
        {
            using var db = Connection;
            var error = ValidateEvent(dto, out var title, out var location);
            if (error != null) return BadRequest(new { Message = error });

            var exists = await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM Events WHERE Id = @Id", new { Id = id });
            if (!exists.HasValue) return NotFound(new { Message = "Event not found." });

            var duplicate = await db.ExecuteScalarAsync<int?>(
                @"SELECT Id FROM Events
                  WHERE lower(btrim(Title)) = lower(@Title) AND EventDate = @EventDate AND Id <> @Id",
                new { Title = title, dto.EventDate, Id = id });
            if (duplicate.HasValue)
                return Conflict(new { Message = "An event with that title already exists on that date." });

            await db.ExecuteAsync(
                @"UPDATE Events
                  SET Title = @Title, Description = @Description,
                      EventDate = @EventDate, Location = @Location
                  WHERE Id = @Id",
                new { Title = title, Description = dto.Description, dto.EventDate, Location = location, Id = id });
            return Ok(new { Message = "Event updated successfully" });
        }

        [HttpDelete("events/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteEvent(int id)
        {
            using var db = Connection;
            var affected = await db.ExecuteAsync("DELETE FROM Events WHERE Id = @Id", new { Id = id });
            if (affected == 0) return NotFound(new { Message = "Event not found." });
            return Ok(new { Message = "Event deleted successfully" });
        }

        [HttpPut("events/{id}/rsvp")]
        public async Task<IActionResult> SetEventRsvp(int id, [FromBody] RsvpDto dto)
        {
            using var db = Connection;
            var status = (dto.Status ?? string.Empty).Trim();
            var canonical = EventStatuses.FirstOrDefault(
                s => s.Equals(status, StringComparison.OrdinalIgnoreCase));
            if (canonical == null)
                return BadRequest(new { Message = "Status must be Invited, Attending, Not Attending or Maybe." });

            var exists = await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM Events WHERE Id = @Id", new { Id = id });
            if (!exists.HasValue) return NotFound(new { Message = "Event not found." });

            await db.ExecuteAsync(
                @"INSERT INTO EventParticipants (EventId, UserId, Status)
                  VALUES (@Id, @UserId, @Status)
                  ON CONFLICT (EventId, UserId) DO UPDATE SET Status = EXCLUDED.Status",
                new { Id = id, UserId = StudentAccess.CallerUserId(User), Status = canonical });

            return Ok(new { Message = "Response saved", Status = canonical });
        }

        [HttpDelete("events/{id}/participants/{userId}")]
        public async Task<IActionResult> RemoveEventParticipant(int id, int userId)
        {
            using var db = Connection;
            var caller = StudentAccess.CallerUserId(User);
            var isStaff = User.IsInRole("Admin") || User.IsInRole("Teacher");
            if (!isStaff && caller != userId) return Forbid();

            var affected = await db.ExecuteAsync(
                "DELETE FROM EventParticipants WHERE EventId = @Id AND UserId = @UserId",
                new { Id = id, UserId = userId });
            if (affected == 0) return NotFound(new { Message = "That user is not on this event." });
            return Ok(new { Message = "Response removed" });
        }

        // --- NOTIFICATIONS ---
        // The schema is normalised: notifications(id, title, message, createdat)
        // plus notificationrecipients(notificationid, userid, isread). These
        // endpoints previously read/wrote a flat Notifications.UserId/IsRead
        // that does not exist, so every one of them returned 500.
        private int CurrentUserId()
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(raw, out var id) ? id : 0;
        }

        [HttpGet("notifications")]
        public async Task<IActionResult> GetNotifications()
        {
            using var db = Connection;
            var userId = CurrentUserId();
            var rows = await db.QueryAsync(@"
                SELECT n.id, n.title, n.message, n.createdat, nr.isread
                FROM notifications n
                JOIN notificationrecipients nr ON nr.notificationid = n.id
                WHERE nr.userid = @UserId
                ORDER BY n.createdat DESC",
                new { UserId = userId });
            return Ok(rows);
        }

        [HttpGet("notifications/unread-count")]
        public async Task<IActionResult> GetUnreadNotificationCount()
        {
            using var db = Connection;
            var count = await db.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*) FROM notificationrecipients
                WHERE userid = @UserId AND isread = FALSE",
                new { UserId = CurrentUserId() });
            return Ok(new { UnreadCount = count });
        }

        [HttpPost("notifications")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> CreateNotification([FromBody] CreateNotificationDto dto)
        {
            var title = (dto.Title ?? string.Empty).Trim();
            if (title.Length == 0) return BadRequest("Notification title is required.");

            // Accept either the legacy single UserId or a recipient list.
            var recipients = dto.UserIds is { Count: > 0 }
                ? dto.UserIds.Where(id => id > 0).Distinct().ToList()
                : new List<int>();
            if (dto.UserId > 0 && !recipients.Contains(dto.UserId)) recipients.Add(dto.UserId);
            if (recipients.Count == 0) return BadRequest("At least one recipient is required.");

            using var db = Connection;
            db.Open();
            using var transaction = db.BeginTransaction();

            try
            {
                var notificationId = await db.ExecuteScalarAsync<int>(
                    "INSERT INTO notifications (title, message) VALUES (@Title, @Message) RETURNING id",
                    new { Title = title, dto.Message }, transaction);

                foreach (var userId in recipients)
                {
                    await db.ExecuteAsync(
                        "INSERT INTO notificationrecipients (notificationid, userid, isread) VALUES (@NotificationId, @UserId, FALSE)",
                        new { NotificationId = notificationId, UserId = userId }, transaction);
                }

                transaction.Commit();
                return Ok(new { Message = "Notification sent successfully", Id = notificationId, RecipientCount = recipients.Count });
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpPatch("notifications/{id}/read")]
        public async Task<IActionResult> MarkNotificationAsRead(int id)
        {
            using var db = Connection;
            // Scoped to the caller's recipient row so one user cannot mark
            // another user's notification as read.
            var affected = await db.ExecuteAsync(
                "UPDATE notificationrecipients SET isread = TRUE WHERE notificationid = @Id AND userid = @UserId",
                new { Id = id, UserId = CurrentUserId() });

            if (affected == 0) return NotFound();
            return Ok(new { Message = "Notification marked as read" });
        }

        [HttpPatch("notifications/read-all")]
        public async Task<IActionResult> MarkAllNotificationsAsRead()
        {
            using var db = Connection;
            var affected = await db.ExecuteAsync(
                "UPDATE notificationrecipients SET isread = TRUE WHERE userid = @UserId",
                new { UserId = CurrentUserId() });
            return Ok(new { Message = "All notifications marked as read", Updated = affected });
        }

        [HttpDelete("notifications/{id}")]
        public async Task<IActionResult> DeleteNotification(int id)
        {
            using var db = Connection;
            db.Open();
            using var transaction = db.BeginTransaction();

            try
            {
                var userId = CurrentUserId();
                var isAdmin = User.IsInRole("Admin");

                // Recipients own their delete; admins may delete anything.
                if (!isAdmin)
                {
                    var owns = await db.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM notificationrecipients WHERE notificationid = @Id AND userid = @UserId",
                        new { Id = id, UserId = userId }, transaction);
                    if (owns == 0) return NotFound();
                }

                await db.ExecuteAsync(
                    "DELETE FROM notificationrecipients WHERE notificationid = @Id",
                    new { Id = id }, transaction);
                await db.ExecuteAsync(
                    "DELETE FROM notifications WHERE id = @Id",
                    new { Id = id }, transaction);

                transaction.Commit();
                return Ok(new { Message = "Notification deleted" });
            }
            catch (Exception)
            {
                throw;
            }
        }
    }

    public class CreateAcademicYearDto
    {
        public string Name { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsCurrent { get; set; }
    }

    public class CreateTimetableDto
    {
        public int ClassId { get; set; }
        public int SectionId { get; set; }
        public int SubjectId { get; set; }
        public int TeacherId { get; set; }
        public int TimeSlotId { get; set; }
        public int DayOfWeek { get; set; }
    }

    /// <summary>
    /// A bell-schedule period. Times are strings so the API rejects malformed
    /// input with a message rather than a model-binding 400, and accepts the
    /// `HH:mm` an `&lt;input type="time"&gt;` sends.
    /// </summary>
    public class TimeSlotDto
    {
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        public string? Label { get; set; }
    }

    public class SaveEventDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime EventDate { get; set; }
        public string? Location { get; set; }
    }

    public class RsvpDto
    {
        public string? Status { get; set; }
    }

    public class CreateNotificationDto
    {
        /// <summary>Legacy single-recipient field; still honoured.</summary>
        public int UserId { get; set; }

        /// <summary>Preferred: send the same notification to several users.</summary>
        public List<int> UserIds { get; set; } = new();

        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
