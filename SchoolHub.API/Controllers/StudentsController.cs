using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using SchoolHub.API.Security;
using System.Data;
using System.Security.Claims;

namespace SchoolHub.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class StudentsController : ControllerBase
    {
        private readonly string _connectionString;

        public StudentsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        /// <summary>
        /// The full roster is staff-only. Learners reach their own data through
        /// their dashboard or the scoped /{id}/* routes.
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> GetAllStudents()
        {
            using var db = Connection;
            // The lateral join picks a single enrolment instead of joining every
            // one. A plain LEFT JOIN returned the student once per enrolment row,
            // which rendered four students as eighteen. The unique index from
            // migrations/003 keeps the data clean; this keeps the response
            // correct even if it is not.
            var students = await db.QueryAsync(@"
                SELECT s.Id, s.RollNumber, s.AdmissionDate, u.Id as UserId, u.Username, u.Email, u.IsActive,
                       c.Name as ClassName, sec.Name as SectionName,
                       latest.ClassId as ClassId, latest.SectionId as SectionId
                FROM Students s
                JOIN Users u ON s.UserId = u.Id
                LEFT JOIN LATERAL (
                    SELECT e.ClassId, e.SectionId
                    FROM Enrollments e
                    WHERE e.StudentId = s.Id
                    ORDER BY e.Id DESC
                    LIMIT 1
                ) latest ON TRUE
                LEFT JOIN Classes c ON c.Id = latest.ClassId
                LEFT JOIN Sections sec ON sec.Id = latest.SectionId");
            return Ok(students);
        }

        [HttpGet("search")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> SearchAndFilterStudents([FromQuery] string? query, [FromQuery] int? classId, [FromQuery] int? sectionId)
        {
            using var db = Connection;
            // Same single-enrolment lateral join as above. SELECT DISTINCT used
            // to paper over the duplicate rows, but it silently chose one
            // arbitrary ClassName/SectionName when a student had several.
            var sql = @"
                SELECT s.Id, s.RollNumber, s.AdmissionDate, u.Id as UserId, u.Username, u.Email, u.IsActive,
                       c.Name as ClassName, sec.Name as SectionName,
                       latest.ClassId as ClassId, latest.SectionId as SectionId
                FROM Students s
                JOIN Users u ON s.UserId = u.Id
                LEFT JOIN LATERAL (
                    SELECT e.ClassId, e.SectionId
                    FROM Enrollments e
                    WHERE e.StudentId = s.Id
                    ORDER BY e.Id DESC
                    LIMIT 1
                ) latest ON TRUE
                LEFT JOIN Classes c ON c.Id = latest.ClassId
                LEFT JOIN Sections sec ON sec.Id = latest.SectionId
                WHERE (@Query IS NULL OR u.Username ILIKE '%' || @Query || '%' OR u.Email ILIKE '%' || @Query || '%' OR s.RollNumber ILIKE '%' || @Query || '%')
                  AND (@ClassId IS NULL OR latest.ClassId = @ClassId)
                  AND (@SectionId IS NULL OR latest.SectionId = @SectionId)";
            var students = await db.QueryAsync(sql, new { Query = query, ClassId = classId, SectionId = sectionId });
            return Ok(students);
        }

        /// <summary>
        /// A single student record. Unlike the roster above, this is reachable by
        /// every role because a Student needs their own row and a Parent needs
        /// their children; ownership is enforced by StudentAccess rather than by
        /// the role attribute, which would have rejected them before the check
        /// could run.
        /// </summary>
        [HttpGet("{id}")]
        [Authorize(Roles = "Admin,Teacher,Student,Parent")]
        public async Task<IActionResult> GetStudentById(int id)
        {
            using var db = Connection;
            if (!await StudentAccess.CanReadStudentAsync(db, User, id)) return Forbid();
            // Same projection as the roster, so opening one student returns the
            // same shape the list does. It previously omitted ClassName and
            // SectionName, so a detail view lost the student's class.
            var sql = @"
                SELECT s.Id, s.RollNumber, s.AdmissionDate, u.Id as UserId, u.Username, u.Email, u.IsActive,
                       c.Name as ClassName, sec.Name as SectionName,
                       latest.ClassId as ClassId, latest.SectionId as SectionId
                FROM Students s
                JOIN Users u ON s.UserId = u.Id
                LEFT JOIN LATERAL (
                    SELECT e.ClassId, e.SectionId
                    FROM Enrollments e
                    WHERE e.StudentId = s.Id
                    ORDER BY e.Id DESC
                    LIMIT 1
                ) latest ON TRUE
                LEFT JOIN Classes c ON c.Id = latest.ClassId
                LEFT JOIN Sections sec ON sec.Id = latest.SectionId
                WHERE s.Id = @Id";
            var student = await db.QueryFirstOrDefaultAsync(sql, new { Id = id });
            if (student == null) return NotFound();
            return Ok(student);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateStudent([FromBody] CreateStudentDto dto)
        {
            using var db = Connection;
            db.Open();
            using var transaction = db.BeginTransaction();

            try
            {
                var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
                var userSql = @"
                    INSERT INTO Users (Username, Email, PasswordHash, Role, IsActive) 
                    VALUES (@Username, @Email, @PasswordHash, 'Student', TRUE) 
                    RETURNING Id;";
                var userId = await db.ExecuteScalarAsync<int>(userSql, new { dto.Username, dto.Email, PasswordHash = passwordHash }, transaction);

                var roleSql = "INSERT INTO UserRoles (UserId, RoleId) SELECT @UserId, Id FROM Roles WHERE Name = 'Student'";
                await db.ExecuteAsync(roleSql, new { UserId = userId }, transaction);

                var studentSql = @"
                    INSERT INTO Students (UserId, RollNumber, AdmissionDate) 
                    VALUES (@UserId, @RollNumber, @AdmissionDate) 
                    RETURNING Id;";
                var studentId = await db.ExecuteScalarAsync<int>(studentSql, new { UserId = userId, dto.RollNumber, AdmissionDate = dto.AdmissionDate ?? DateTime.UtcNow }, transaction);

                if (dto.ClassId.HasValue && dto.SectionId.HasValue)
                {
                    var enrollSql = @"
                        INSERT INTO Enrollments (StudentId, ClassId, SectionId) 
                        VALUES (@StudentId, @ClassId, @SectionId)";
                    await db.ExecuteAsync(enrollSql, new { StudentId = studentId, dto.ClassId, dto.SectionId }, transaction);
                }

                // Audit Log
                var adminUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                int.TryParse(adminUserIdStr, out int adminUserId);
                await db.ExecuteAsync("INSERT INTO AuditLogs (UserId, Action, Details) VALUES (@UserId, 'CREATE_STUDENT', @Details)",
                    new { UserId = adminUserId, Details = $"Created student {dto.Username}" }, transaction);

                transaction.Commit();
                return Ok(new { Message = "Student created successfully", StudentId = studentId, UserId = userId });
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateStudent(int id, [FromBody] UpdateStudentDto dto)
        {
            var roll = (dto.RollNumber ?? string.Empty).Trim();
            if (roll.Length == 0) return BadRequest(new { Message = "Roll number is required." });

            using var db = Connection;
            db.Open();

            // Without this the UPDATE matched nothing and still returned 200, so
            // the UI reported success for a student that does not exist.
            var exists = await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM Students WHERE Id = @Id", new { Id = id });
            if (!exists.HasValue) return NotFound();

            await db.ExecuteAsync(
                "UPDATE Students SET RollNumber = @RollNumber WHERE Id = @Id",
                new { RollNumber = roll, Id = id });

            return Ok(new { Message = "Student updated successfully" });
        }

        [HttpPatch("{id}/deactivate")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeactivateStudent(int id)
        {
            using var db = Connection;
            var userId = await db.ExecuteScalarAsync<int?>("SELECT UserId FROM Students WHERE Id = @Id", new { Id = id });
            if (!userId.HasValue) return NotFound();

            await db.ExecuteAsync("UPDATE Users SET IsActive = FALSE WHERE Id = @UserId", new { UserId = userId.Value });
            return Ok(new { Message = "Student deactivated successfully" });
        }

        /// <summary>
        /// Re-enables a deactivated account. Deactivation is otherwise a one-way
        /// door: without this, a student removed by mistake can only be fixed by
        /// editing the database directly.
        /// </summary>
        [HttpPatch("{id}/reactivate")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ReactivateStudent(int id)
        {
            using var db = Connection;
            var userId = await db.ExecuteScalarAsync<int?>("SELECT UserId FROM Students WHERE Id = @Id", new { Id = id });
            if (!userId.HasValue) return NotFound();

            await db.ExecuteAsync("UPDATE Users SET IsActive = TRUE WHERE Id = @UserId", new { UserId = userId.Value });
            return Ok(new { Message = "Student reactivated successfully" });
        }

        [HttpPost("{id}/assign-class")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AssignStudentToClass(int id, [FromBody] AssignClassDto dto)
        {
            using var db = Connection;
            db.Open();

            // Same reason as UpdateStudent: assigning a class to a student that
            // does not exist used to report success.
            var exists = await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM Students WHERE Id = @Id", new { Id = id });
            if (!exists.HasValue) return NotFound();

            if (dto.ClassId <= 0 || dto.SectionId <= 0)
            {
                return BadRequest(new { Message = "Both a class and a section are required." });
            }

            // Atomic upsert. The previous SELECT-then-INSERT/UPDATE ran two
            // round trips and could race, and the UPDATE variant rewrote every
            // enrolment row for the student. ON CONFLICT relies on the unique
            // index added in migrations/003_enrollment_one_per_student.sql.
            await db.ExecuteAsync(
                """
                INSERT INTO Enrollments (StudentId, ClassId, SectionId)
                VALUES (@StudentId, @ClassId, @SectionId)
                ON CONFLICT (StudentId) DO UPDATE
                    SET ClassId = @ClassId, SectionId = @SectionId
                """,
                new { StudentId = id, dto.ClassId, dto.SectionId });

            return Ok(new { Message = "Student assigned to class successfully" });
        }

        [HttpGet("{id}/attendance")]
        public async Task<IActionResult> GetStudentAttendance(int id)
        {
            using var db = Connection;
            if (!await StudentAccess.CanReadStudentAsync(db, User, id)) return Forbid();
            var sql = @"
                SELECT ar.Id, ar.Status, ar.Remarks, s.Date, c.Name as ClassName
                FROM AttendanceRecords ar
                JOIN AttendanceSessions s ON ar.SessionId = s.Id
                JOIN Classes c ON s.ClassId = c.Id
                WHERE ar.StudentId = @StudentId";
            return Ok(await db.QueryAsync(sql, new { StudentId = id }));
        }

        [HttpGet("{id}/results")]
        public async Task<IActionResult> GetStudentResults(int id)
        {
            using var db = Connection;
            if (!await StudentAccess.CanReadStudentAsync(db, User, id)) return Forbid();
            var sql = @"
                SELECT m.Id, m.MarksObtained, m.Grade, m.Remarks, es.MaxMarks, sub.Name as SubjectName, ex.Title as ExamTitle
                FROM Marks m
                JOIN ExamSubjects es ON m.ExamSubjectId = es.Id
                JOIN Subjects sub ON es.SubjectId = sub.Id
                JOIN Exams ex ON es.ExamId = ex.Id
                WHERE m.StudentId = @StudentId";
            return Ok(await db.QueryAsync(sql, new { StudentId = id }));
        }

        [HttpGet("{id}/fees")]
        public async Task<IActionResult> GetStudentFees(int id)
        {
            using var db = Connection;
            if (!await StudentAccess.CanReadStudentAsync(db, User, id)) return Forbid();
            var sql = @"
                SELECT sf.Id, sf.DueDate, sf.Status, fs.Name as FeeName, fs.Amount
                FROM StudentFees sf
                JOIN FeeStructures fs ON sf.FeeStructureId = fs.Id
                WHERE sf.StudentId = @StudentId";
            return Ok(await db.QueryAsync(sql, new { StudentId = id }));
        }

        [HttpGet("{id}/assignments")]
        public async Task<IActionResult> GetStudentAssignments(int id)
        {
            using var db = Connection;
            if (!await StudentAccess.CanReadStudentAsync(db, User, id)) return Forbid();
            var sql = @"
                SELECT a.Id, a.Title, a.Description, a.DueDate, a.MaxScore, sub.Name as SubjectName,
                       subm.FilePath, subm.SubmittedAt, subm.Score, subm.Feedback
                FROM Assignments a
                JOIN Subjects sub ON a.SubjectId = sub.Id
                LEFT JOIN AssignmentSubmissions subm ON a.Id = subm.AssignmentId AND subm.StudentId = @StudentId";
            return Ok(await db.QueryAsync(sql, new { StudentId = id }));
        }
    }

    public class CreateStudentDto
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string RollNumber { get; set; } = string.Empty;
        public DateTime? AdmissionDate { get; set; }
        public int? ClassId { get; set; }
        public int? SectionId { get; set; }
    }

    public class UpdateStudentDto
    {
        public string RollNumber { get; set; } = string.Empty;
    }

    public class AssignClassDto
    {
        public int ClassId { get; set; }
        public int SectionId { get; set; }
    }
}
