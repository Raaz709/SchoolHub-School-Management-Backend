using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using SchoolHub.API.Security;
using System.Data;

namespace SchoolHub.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AttendanceController : ControllerBase
    {
        /// <summary>
        /// The only statuses the schema allows. Previously this was free text, so a
        /// typo silently became a fifth status that no report counted and the
        /// student's percentage quietly went up.
        /// </summary>
        public static readonly string[] Statuses = { "Present", "Absent", "Late", "Excused" };

        private readonly string _connectionString;

        public AttendanceController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        /* ------------------------------ read ------------------------------ */

        /// <summary>
        /// Sessions for marking, newest first. Admin and Teacher may both read
        /// these: attendance is marked per class and section, and the section list
        /// is already scoped to the caller by the roster endpoint.
        /// </summary>
        [HttpGet("sessions")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> GetSessions(
            [FromQuery] int? classId,
            [FromQuery] int? sectionId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to)
        {
            using var db = Connection;

            // Marked/Total are counted in one pass rather than fetched per session,
            // so the history list costs a single round trip.
            var sessions = await db.QueryAsync(@"
                SELECT s.Id,
                       s.Date,
                       s.ClassId,
                       s.SectionId,
                       s.TeacherId,
                       c.Name AS ClassName,
                       sec.Name AS SectionName,
                       u.Username AS TeacherName,
                       count(ar.Id) FILTER (WHERE ar.Status <> 'Absent') AS Marked,
                       count(ar.Id) AS Total
                FROM AttendanceSessions s
                JOIN Classes c ON s.ClassId = c.Id
                JOIN Sections sec ON s.SectionId = sec.Id
                LEFT JOIN Teachers t ON s.TeacherId = t.Id
                LEFT JOIN Users u ON t.UserId = u.Id
                LEFT JOIN AttendanceRecords ar ON ar.SessionId = s.Id
                -- @From/@To are compared against a date column and are cast
                  -- explicitly: when either is omitted the parameter arrives null
                  -- and PostgreSQL cannot infer its type, which failed the whole
                  -- query rather than skipping the filter.
                WHERE (@ClassId::int IS NULL OR s.ClassId = @ClassId)
                  AND (@SectionId::int IS NULL OR s.SectionId = @SectionId)
                  AND (@From::date IS NULL OR s.Date >= @From::date)
                  AND (@To::date IS NULL OR s.Date <= @To::date)
                GROUP BY s.Id, s.Date, s.ClassId, s.SectionId, s.TeacherId, c.Name, sec.Name, u.Username
                ORDER BY s.Date DESC, s.Id DESC", new { ClassId = classId, SectionId = sectionId, From = from, To = to });

            return Ok(sessions);
        }

        /// <summary>
        /// One session with every record, used to re-open a past marking for
        /// correction.
        /// </summary>
        [HttpGet("sessions/{id}")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> GetSession(int id)
        {
            using var db = Connection;

            var session = await db.QueryFirstOrDefaultAsync(@"
                SELECT s.Id,
                       s.Date,
                       s.ClassId,
                       s.SectionId,
                       s.TeacherId,
                       c.Name AS ClassName,
                       sec.Name AS SectionName
                FROM AttendanceSessions s
                JOIN Classes c ON s.ClassId = c.Id
                JOIN Sections sec ON s.SectionId = sec.Id
                WHERE s.Id = @Id", new { Id = id });
            if (session == null) return NotFound(new { Message = "That attendance session does not exist." });

            var records = await db.QueryAsync(@"
                SELECT ar.Id, ar.StudentId, ar.Status, ar.Remarks,
                       stu.RollNumber,
                       u.Username
                FROM AttendanceRecords ar
                JOIN Students stu ON ar.StudentId = stu.Id
                JOIN Users u ON stu.UserId = u.Id
                WHERE ar.SessionId = @Id
                ORDER BY stu.RollNumber", new { Id = id });

            return Ok(new { Session = session, Records = records });
        }

        /// <summary>
        /// The students to mark, given a class and section. Any missing record from
        /// a previous session is returned with a null status so the marking screen
        /// can pre-fill what was already decided instead of asking the teacher to
        /// re-enter it.
        /// </summary>
        [HttpGet("roster")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> GetRoster(
            [FromQuery] int classId,
            [FromQuery] int sectionId,
            [FromQuery] DateTime? date)
        {
            using var db = Connection;

            if (!(await db.QueryFirstOrDefaultAsync<int?>(
                    "SELECT Id FROM Classes WHERE Id = @Id", new { Id = classId })).HasValue)
                return NotFound(new { Message = "That class does not exist." });

            var section = await db.QueryFirstOrDefaultAsync<(int Id, int ClassId)?>(@"
                SELECT s.Id, s.ClassId
                FROM Sections s
                WHERE s.Id = @Id", new { Id = sectionId });
            if (section == null) return NotFound(new { Message = "That section does not exist." });

            // A section belongs to exactly one class. Marking the section while
            // sending a different class would file the session under the wrong
            // grade and silently drop it from the real one.
            if (section.Value.ClassId != classId)
                return BadRequest(new { Message = "That section does not belong to the selected class." });

            // LEFT JOIN the existing session so a re-mark starts from what is stored.
            var roster = await db.QueryAsync(@"
                SELECT s.Id AS StudentId,
                       s.RollNumber,
                       u.Username,
                       ar.Id AS RecordId,
                       ar.Status,
                       ar.Remarks
                FROM Enrollments e
                JOIN Students s ON e.StudentId = s.Id
                JOIN Users u ON s.UserId = u.Id
                LEFT JOIN LATERAL (
                    SELECT r.Id, r.Status, r.Remarks
                    FROM AttendanceRecords r
                    WHERE r.StudentId = s.Id
                      AND r.SessionId = (
                          SELECT sess.Id
                          FROM AttendanceSessions sess
                          WHERE sess.ClassId = @ClassId AND sess.SectionId = @SectionId AND sess.Date = @Date
                          LIMIT 1
                      )
                ) ar ON TRUE
                WHERE e.ClassId = @ClassId AND e.SectionId = @SectionId
                ORDER BY s.RollNumber",
                new { ClassId = classId, SectionId = sectionId, Date = date ?? DateTime.Today });

            return Ok(new { ClassId = classId, SectionId = sectionId, Date = date ?? DateTime.Today, Students = roster });
        }

        /// <summary>
        /// The caller's own attendance, for a Student.
        ///
        /// The student page previously had to know its own Students.Id to build
        /// the by-id request, and nothing exposed it: /api/profile returns the
        /// user row only. This resolves the id from the caller's token instead, so
        /// the page cannot show an empty list by asking for the wrong student.
        /// </summary>
        [HttpGet("mine")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> GetMyAttendance()
        {
            using var db = Connection;

            var studentId = await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM Students WHERE UserId = @UserId",
                new { UserId = StudentAccess.CallerUserId(User) });
            if (!studentId.HasValue)
                return NotFound(new { Message = "This account is not linked to a student record." });

            return Ok(await QueryStudentAttendanceAsync(db, studentId.Value));
        }

        /// <summary>
        /// One student's own attendance. Admin and Teacher may read any student;
        /// a Student only their own record and a Parent only a linked child, so
        /// changing the id in the URL is not enough to read someone else's.
        /// </summary>
        [HttpGet("student/{studentId}")]
        [Authorize]
        public async Task<IActionResult> GetStudentAttendance(int studentId)
        {
            using var db = Connection;
            if (!await StudentAccess.CanReadStudentAsync(db, User, studentId)) return Forbid();

            return Ok(await QueryStudentAttendanceAsync(db, studentId));
        }

        /// <summary>
        /// One student's attendance with the class and section names attached, so
        /// the student and parent pages can label each day without pairing it
        /// against a class list they were never given.
        /// </summary>
        private static async Task<IEnumerable<dynamic>> QueryStudentAttendanceAsync(
            IDbConnection db,
            int studentId)
        {
            return await db.QueryAsync(@"
                SELECT ar.Id,
                       ar.Status,
                       ar.Remarks,
                       ar.SessionId,
                       s.Date,
                       s.ClassId,
                       c.Name AS ClassName,
                       sec.Name AS SectionName
                FROM AttendanceRecords ar
                JOIN AttendanceSessions s ON ar.SessionId = s.Id
                LEFT JOIN Classes c ON s.ClassId = c.Id
                LEFT JOIN Sections sec ON s.SectionId = sec.Id
                WHERE ar.StudentId = @StudentId
                ORDER BY s.Date DESC, ar.Id DESC", new { StudentId = studentId });
        }

        /* ----------------------------- writes ----------------------------- */

        /// <summary>
        /// Records a session and every mark in one transaction, so a rejected row
        /// never leaves a half-marked session behind.
        /// </summary>
        [HttpPost("session")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> CreateAttendanceSession([FromBody] CreateAttendanceSessionDto dto)
        {
            var problem = await ValidateAsync(dto);
            if (problem != null) return problem;

            using var db = Connection;
            db.Open();
            using var transaction = db.BeginTransaction();

            try
            {
                var userId = StudentAccess.CallerUserId(User);
                // Admins do not have a Teachers row, so TeacherId is null for them
                // and the session is attributed by name in the UI instead.
                var teacherId = await db.ExecuteScalarAsync<int?>(
                    "SELECT Id FROM Teachers WHERE UserId = @UserId", new { UserId = userId }, transaction);

                var existing = await db.ExecuteScalarAsync<int?>(@"
                    SELECT Id FROM AttendanceSessions
                    WHERE ClassId = @ClassId AND SectionId = @SectionId AND Date = @Date",
                    new { ClassId = dto.ClassId, SectionId = dto.SectionId, Date = dto.Date.Date }, transaction);
                if (existing.HasValue)
                {
                    return Conflict(new
                    {
                        Message = "Attendance has already been marked for that section on that date. Edit the existing session instead.",
                        SessionId = existing.Value,
                    });
                }

                var sessionId = await InsertSessionAsync(db, transaction, dto, teacherId);

                await db.ExecuteAsync(@"
                    INSERT INTO AuditLogs (UserId, Action, Details)
                    VALUES (@UserId, 'MARK_ATTENDANCE', @Details)",
                    new
                    {
                        UserId = userId,
                        Details = $"Marked attendance for section {dto.SectionId} on {dto.Date:yyyy-MM-dd} ({dto.Records.Count} student(s))",
                    }, transaction);

                transaction.Commit();
                return Ok(new { Message = "Attendance marked successfully", SessionId = sessionId });
            }
            catch
            {
                // The transaction rolls back on dispose; the rethrow lets
                // ExceptionMiddleware log and classify it.
                throw;
            }
        }

        /// <summary>
        /// Replaces every mark on an existing session. Records are matched by
        /// student id rather than re-inserted, so editing a session cannot
        /// duplicate a student or orphan their original remark.
        /// </summary>
        [HttpPut("sessions/{id}")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> UpdateSession(int id, [FromBody] CreateAttendanceSessionDto dto)
        {
            var problem = await ValidateAsync(dto);
            if (problem != null) return problem;

            using var db = Connection;
            db.Open();
            using var transaction = db.BeginTransaction();

            try
            {
                var existing = await db.QueryFirstOrDefaultAsync<SessionRow>(
                    "SELECT ClassId, SectionId, Date FROM AttendanceSessions WHERE Id = @Id",
                    new { Id = id }, transaction);
                if (existing == null)
                    return NotFound(new { Message = "That attendance session does not exist." });

                // Moving a session to another section would silently orphan the
                // students who were in the old one.
                if (existing.ClassId != dto.ClassId || existing.SectionId != dto.SectionId)
                    return BadRequest(new { Message = "A session cannot be moved to a different class or section. Mark the new one instead." });

                // The unique index covers this too, but catching it here names the
                // conflicting date instead of surfacing a bare conflict.
                var clash = await db.ExecuteScalarAsync<int?>(@"
                    SELECT Id FROM AttendanceSessions
                    WHERE ClassId = @ClassId AND SectionId = @SectionId AND Date = @Date AND Id <> @Id",
                    new { ClassId = dto.ClassId, SectionId = dto.SectionId, Date = dto.Date.Date, Id = id }, transaction);
                if (clash.HasValue)
                {
                    return Conflict(new
                    {
                        Message = "Another session already exists for that section on that date.",
                        SessionId = clash.Value,
                    });
                }

                await db.ExecuteAsync(
                    "UPDATE AttendanceSessions SET Date = @Date WHERE Id = @Id",
                    new { Date = dto.Date.Date, Id = id }, transaction);

                foreach (var rec in dto.Records)
                {
                    await db.ExecuteAsync(@"
                        UPDATE AttendanceRecords
                        SET Status = @Status, Remarks = @Remarks
                        WHERE SessionId = @Id AND StudentId = @StudentId",
                        new { Status = rec.Status, Remarks = rec.Remarks, Id = id, StudentId = rec.StudentId },
                        transaction);
                }

                var userId = StudentAccess.CallerUserId(User);
                await db.ExecuteAsync(@"
                    INSERT INTO AuditLogs (UserId, Action, Details)
                    VALUES (@UserId, 'EDIT_ATTENDANCE', @Details)",
                    new
                    {
                        UserId = userId,
                        Details = $"Edited attendance session {id} on {dto.Date:yyyy-MM-dd} ({dto.Records.Count} student(s))",
                    }, transaction);

                transaction.Commit();
                return Ok(new { Message = "Attendance updated successfully", SessionId = id });
            }
            catch
            {
                throw;
            }
        }

        /* ----------------------------- helpers ----------------------------- */

        /// <summary>
        /// Shared checks for create and update. Returns null when the payload is
        /// usable, otherwise the ready-to-return 400.
        /// </summary>
        private async Task<ActionResult?> ValidateAsync(CreateAttendanceSessionDto dto)
        {
            if (dto.ClassId <= 0) return BadRequest(new { Message = "A class is required." });
            if (dto.SectionId <= 0) return BadRequest(new { Message = "A section is required." });
            if (dto.Date == default) return BadRequest(new { Message = "A date is required." });

            using var db = Connection;

            if (!(await db.QueryFirstOrDefaultAsync<int?>("SELECT Id FROM Classes WHERE Id = @Id", new { Id = dto.ClassId })).HasValue)
                return NotFound(new { Message = "That class does not exist." });

            var section = await db.QueryFirstOrDefaultAsync<(int Id, int ClassId)?>(@"
                SELECT s.Id, s.ClassId FROM Sections s WHERE s.Id = @Id", new { Id = dto.SectionId });
            if (section == null) return NotFound(new { Message = "That section does not exist." });
            if (section.Value.ClassId != dto.ClassId)
                return BadRequest(new { Message = "That section does not belong to the selected class." });

            if (dto.Records.Count == 0)
                return BadRequest(new { Message = "Mark at least one student." });

            // The same student twice would fail the unique index mid-transaction
            // with no usable message, so it is caught here with the id named.
            var seen = new HashSet<int>();
            foreach (var rec in dto.Records)
            {
                if (!seen.Add(rec.StudentId))
                    return BadRequest(new { Message = $"Student {rec.StudentId} appears more than once in this session." });

                var status = (rec.Status ?? string.Empty).Trim();
                if (!Statuses.Contains(status, StringComparer.Ordinal))
                {
                    return BadRequest(new
                    {
                        Message = $"\"{status}\" is not a valid attendance status.",
                        Allowed = Statuses,
                    });
                }
            }

            // Every mark has to belong to the section being marked, otherwise a
            // roster from another grade could be filed under this one.
            var ids = dto.Records.Select(r => r.StudentId).ToArray();
            var enrolled = (await db.QueryAsync<int>(
                "SELECT StudentId FROM Enrollments WHERE ClassId = @ClassId AND SectionId = @SectionId AND StudentId = ANY(@Ids)",
                new { ClassId = dto.ClassId, SectionId = dto.SectionId, Ids = ids })).ToHashSet();
            var strays = ids.Where(x => !enrolled.Contains(x)).ToArray();
            if (strays.Length > 0)
            {
                return BadRequest(new
                {
                    Message = $"Student id(s) {string.Join(", ", strays)} are not enrolled in that section.",
                });
            }

            return null;
        }

        private static async Task<int> InsertSessionAsync(
            IDbConnection db,
            IDbTransaction transaction,
            CreateAttendanceSessionDto dto,
            int? teacherId)
        {
            var sessionId = await db.ExecuteScalarAsync<int>(@"
                INSERT INTO AttendanceSessions (ClassId, SectionId, TeacherId, Date)
                VALUES (@ClassId, @SectionId, @TeacherId, @Date)
                RETURNING Id",
                new { ClassId = dto.ClassId, SectionId = dto.SectionId, TeacherId = teacherId, Date = dto.Date.Date },
                transaction);

            foreach (var rec in dto.Records)
            {
                await db.ExecuteAsync(@"
                    INSERT INTO AttendanceRecords (SessionId, StudentId, Status, Remarks)
                    VALUES (@SessionId, @StudentId, @Status, @Remarks)",
                    new
                    {
                        SessionId = sessionId,
                        StudentId = rec.StudentId,
                        Status = rec.Status.Trim(),
                        Remarks = rec.Remarks ?? string.Empty,
                    },
                    transaction);
            }

            return sessionId;
        }

        private class SessionRow
        {
            public int ClassId { get; set; }
            public int SectionId { get; set; }
            public DateTime Date { get; set; }
        }
    }

    public class CreateAttendanceSessionDto
    {
        public int ClassId { get; set; }
        public int SectionId { get; set; }
        public DateTime Date { get; set; }
        public List<AttendanceRecordItemDto> Records { get; set; } = new();
    }

    public class AttendanceRecordItemDto
    {
        public int StudentId { get; set; }
        /// <summary>One of Present, Absent, Late, Excused. Validated server-side.</summary>
        public string Status { get; set; } = "Present";
        public string Remarks { get; set; } = string.Empty;
    }
}