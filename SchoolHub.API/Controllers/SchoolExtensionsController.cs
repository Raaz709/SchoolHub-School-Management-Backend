using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;
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

        // --- TIMETABLE ---
        [HttpGet("timetable")]
        public async Task<IActionResult> GetTimetable([FromQuery] int classId, [FromQuery] int sectionId)
        {
            using var db = Connection;
            var sql = @"
                SELECT t.Id, t.DayOfWeek, ts.StartTime, ts.EndTime, sub.Name as SubjectName, u.Username as TeacherName
                FROM TimetableEntries t
                JOIN TimeSlots ts ON t.TimeSlotId = ts.Id
                JOIN Subjects sub ON t.SubjectId = sub.Id
                LEFT JOIN Teachers th ON t.TeacherId = th.Id
                LEFT JOIN Users u ON th.UserId = u.Id
                WHERE t.ClassId = @ClassId AND t.SectionId = @SectionId";
            return Ok(await db.QueryAsync(sql, new { ClassId = classId, SectionId = sectionId }));
        }

        [HttpPost("timetable")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateTimetableEntry([FromBody] CreateTimetableDto dto)
        {
            using var db = Connection;
            var sql = @"
                INSERT INTO TimetableEntries (ClassId, SectionId, SubjectId, TeacherId, TimeSlotId, DayOfWeek) 
                VALUES (@ClassId, @SectionId, @SubjectId, @TeacherId, @TimeSlotId, @DayOfWeek) 
                RETURNING Id;";
            var id = await db.ExecuteScalarAsync<int>(sql, dto);
            return Ok(new { Message = "Timetable entry created successfully", Id = id });
        }

        // --- EVENTS ---
        [HttpGet("events")]
        public async Task<IActionResult> GetEvents()
        {
            using var db = Connection;
            return Ok(await db.QueryAsync("SELECT * FROM Events ORDER BY EventDate ASC"));
        }

        [HttpPost("events")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateEvent([FromBody] CreateEventDto dto)
        {
            using var db = Connection;
            var sql = "INSERT INTO Events (Title, Description, EventDate, Location) VALUES (@Title, @Description, @EventDate, @Location) RETURNING Id;";
            var id = await db.ExecuteScalarAsync<int>(sql, dto);
            return Ok(new { Message = "Event created successfully", Id = id });
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

    public class CreateEventDto
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public string Location { get; set; } = string.Empty;
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
