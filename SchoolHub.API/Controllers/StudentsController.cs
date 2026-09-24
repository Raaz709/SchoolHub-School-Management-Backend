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
    public class StudentsController : ControllerBase
    {
        private readonly string _connectionString;

        public StudentsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        [HttpGet]
        public async Task<IActionResult> GetAllStudents()
        {
            using var db = Connection;
            var sql = @"
                SELECT s.Id, s.RollNumber, s.AdmissionDate, u.Id as UserId, u.Username, u.Email, u.IsActive,
                       c.Name as ClassName, sec.Name as SectionName
                FROM Students s
                JOIN Users u ON s.UserId = u.Id
                LEFT JOIN Enrollments e ON s.Id = e.StudentId
                LEFT JOIN Classes c ON e.ClassId = c.Id
                LEFT JOIN Sections sec ON e.SectionId = sec.Id";
            var students = await db.QueryAsync(sql);
            return Ok(students);
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchAndFilterStudents([FromQuery] string? query, [FromQuery] int? classId, [FromQuery] int? sectionId)
        {
            using var db = Connection;
            var sql = @"
                SELECT DISTINCT s.Id, s.RollNumber, s.AdmissionDate, u.Id as UserId, u.Username, u.Email, u.IsActive,
                       c.Name as ClassName, sec.Name as SectionName
                FROM Students s
                JOIN Users u ON s.UserId = u.Id
                LEFT JOIN Enrollments e ON s.Id = e.StudentId
                LEFT JOIN Classes c ON e.ClassId = c.Id
                LEFT JOIN Sections sec ON e.SectionId = sec.Id
                WHERE (@Query IS NULL OR u.Username ILIKE '%' || @Query || '%' OR u.Email ILIKE '%' || @Query || '%' OR s.RollNumber ILIKE '%' || @Query || '%')
                  AND (@ClassId IS NULL OR e.ClassId = @ClassId)
                  AND (@SectionId IS NULL OR e.SectionId = @SectionId)";
            var students = await db.QueryAsync(sql, new { Query = query, ClassId = classId, SectionId = sectionId });
            return Ok(students);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetStudentById(int id)
        {
            using var db = Connection;
            var sql = @"
                SELECT s.Id, s.RollNumber, s.AdmissionDate, u.Id as UserId, u.Username, u.Email, u.IsActive
                FROM Students s
                JOIN Users u ON s.UserId = u.Id
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
                    INSERT INTO Users (Username, Email, PasswordHash, IsActive) 
                    VALUES (@Username, @Email, @PasswordHash, TRUE) 
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
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateStudent(int id, [FromBody] UpdateStudentDto dto)
        {
            using var db = Connection;
            var sql = "UPDATE Students SET RollNumber = @RollNumber WHERE Id = @Id";
            await db.ExecuteAsync(sql, new { dto.RollNumber, Id = id });
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

        [HttpPost("{id}/assign-class")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AssignStudentToClass(int id, [FromBody] AssignClassDto dto)
        {
            using var db = Connection;
            var existing = await db.ExecuteScalarAsync<int?>("SELECT Id FROM Enrollments WHERE StudentId = @StudentId", new { StudentId = id });
            if (existing.HasValue)
            {
                await db.ExecuteAsync("UPDATE Enrollments SET ClassId = @ClassId, SectionId = @SectionId WHERE StudentId = @StudentId",
                    new { dto.ClassId, dto.SectionId, StudentId = id });
            }
            else
            {
                await db.ExecuteAsync("INSERT INTO Enrollments (StudentId, ClassId, SectionId) VALUES (@StudentId, @ClassId, @SectionId)",
                    new { StudentId = id, dto.ClassId, dto.SectionId });
            }
            return Ok(new { Message = "Student assigned to class successfully" });
        }

        [HttpGet("{id}/attendance")]
        public async Task<IActionResult> GetStudentAttendance(int id)
        {
            using var db = Connection;
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
