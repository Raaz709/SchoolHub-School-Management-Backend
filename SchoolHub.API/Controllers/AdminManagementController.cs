using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;
using System.Security.Claims;

namespace SchoolHub.API.Controllers
{
    [Route("api/admin")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminManagementController : ControllerBase
    {
        private readonly string _connectionString;

        public AdminManagementController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

            // --- DEPARTMENTS ---
            // Teachers reference Departments, but there was no way for a client to
            // list them, so the teacher form had to take a raw numeric id.
            [HttpGet("departments")]
            public async Task<IActionResult> GetDepartments()
            {
                using var db = Connection;
                var departments = await db.QueryAsync(
                    "SELECT id, name FROM departments ORDER BY name");
                return Ok(departments);
            }

            [HttpPost("departments")]
            public async Task<IActionResult> CreateDepartment([FromBody] CreateDepartmentDto dto)
            {
                var name = (dto.Name ?? string.Empty).Trim();
                if (name.Length == 0) return BadRequest("Department name is required.");

                try
                {
                    using var db = Connection;
                    var id = await db.ExecuteScalarAsync<int>(
                        "INSERT INTO departments (name) VALUES (@Name) RETURNING id",
                        new { Name = name });
                    return Ok(new { Message = "Department created successfully", DepartmentId = id });
                }
                catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
                {
                    return Conflict("A department with that name already exists.");
                }
            }

            // --- TEACHER MANAGEMENT ---
        [HttpGet("teachers")]
        public async Task<IActionResult> GetTeachers()
        {
            using var db = Connection;
            var sql = @"
                  SELECT t.Id, t.EmployeeCode, t.HireDate, t.DepartmentId, d.Name as DepartmentName, u.Id as UserId, u.Username, u.Email, u.IsActive
                FROM Teachers t
                JOIN Users u ON t.UserId = u.Id
                LEFT JOIN Departments d ON t.DepartmentId = d.Id";
            return Ok(await db.QueryAsync(sql));
        }

        [HttpPut("teachers/{id}")]
        public async Task<IActionResult> UpdateTeacher(int id, [FromBody] UpdateTeacherDto dto)
        {
            using var db = Connection;
            var sql = "UPDATE Teachers SET DepartmentId = @DepartmentId, EmployeeCode = @EmployeeCode WHERE Id = @Id";
            await db.ExecuteAsync(sql, new { dto.DepartmentId, dto.EmployeeCode, Id = id });
            return Ok(new { Message = "Teacher updated successfully" });
        }

        [HttpPatch("teachers/{id}/deactivate")]
        public async Task<IActionResult> DeactivateTeacher(int id)
        {
            using var db = Connection;
            var userId = await db.ExecuteScalarAsync<int?>("SELECT UserId FROM Teachers WHERE Id = @Id", new { Id = id });
            if (!userId.HasValue) return NotFound();
            await db.ExecuteAsync("UPDATE Users SET IsActive = FALSE WHERE Id = @UserId", new { UserId = userId.Value });
            return Ok(new { Message = "Teacher deactivated successfully" });
        }

        [HttpPost("teachers/{id}/assign-subjects")]
        public async Task<IActionResult> AssignTeacherSubjects(int id, [FromBody] AssignSubjectsDto dto)
        {
            using var db = Connection;
            foreach (var subjectId in dto.SubjectIdList)
            {
                await db.ExecuteAsync("UPDATE Subjects SET TeacherId = @TeacherId WHERE Id = @SubjectId", new { TeacherId = id, SubjectId = subjectId });
            }
            return Ok(new { Message = "Subjects assigned to teacher successfully" });
        }

        // --- PARENT MANAGEMENT ---
        [HttpGet("parents")]
        public async Task<IActionResult> GetParents()
        {
            using var db = Connection;
            var sql = @"
                SELECT p.Id, p.Occupation, u.Id as UserId, u.Username, u.Email, u.IsActive
                FROM Parents p
                JOIN Users u ON p.UserId = u.Id";
            return Ok(await db.QueryAsync(sql));
        }

        [HttpPost("parents")]
        public async Task<IActionResult> CreateParent([FromBody] CreateParentDto dto)
        {
            using var db = Connection;
            db.Open();
            using var transaction = db.BeginTransaction();
            try
            {
                var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
                // See TeachersController.CreateTeacher: the JWT is built from
                // Users.Role, so it must be set on insert, not only in UserRoles.
                var userId = await db.ExecuteScalarAsync<int>(
                    "INSERT INTO Users (Username, Email, PasswordHash, Role, IsActive) VALUES (@Username, @Email, @PasswordHash, 'Parent', TRUE) RETURNING Id;",
                    new { dto.Username, dto.Email, PasswordHash = passwordHash }, transaction);

                await db.ExecuteAsync("INSERT INTO UserRoles (UserId, RoleId) SELECT @UserId, Id FROM Roles WHERE Name = 'Parent'", new { UserId = userId }, transaction);

                var parentId = await db.ExecuteScalarAsync<int>(
                    "INSERT INTO Parents (UserId, Occupation) VALUES (@UserId, @Occupation) RETURNING Id;",
                    new { UserId = userId, dto.Occupation }, transaction);

                transaction.Commit();
                return Ok(new { Message = "Parent created successfully", ParentId = parentId });
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpPost("parents/link-student")]
        public async Task<IActionResult> LinkParentToStudent([FromBody] LinkParentStudentDto dto)
        {
            using var db = Connection;
            await db.ExecuteAsync(
                "INSERT INTO StudentParents (StudentId, ParentId, Relationship) VALUES (@StudentId, @ParentId, @Relationship) ON CONFLICT DO NOTHING",
                new { dto.StudentId, dto.ParentId, dto.Relationship });
            return Ok(new { Message = "Parent linked to student successfully" });
        }

        [HttpDelete("parents/unlink-student")]
        public async Task<IActionResult> UnlinkParentFromStudent([FromBody] LinkParentStudentDto dto)
        {
            using var db = Connection;
            await db.ExecuteAsync(
                "DELETE FROM StudentParents WHERE StudentId = @StudentId AND ParentId = @ParentId",
                new { dto.StudentId, dto.ParentId });
            return Ok(new { Message = "Parent unlinked from student successfully" });
        }

        [HttpGet("parents/{parentID}/children")]
        public async Task<IActionResult> GetParentChildren(int parentID)
        {
            using var db = Connection;
            var sql = @"
                SELECT s.Id, s.RollNumber, u.Username as StudentName, c.Name as ClassName
                FROM StudentParents sp
                JOIN Students s ON sp.StudentId = s.Id
                JOIN Users u ON s.UserId = u.Id
                LEFT JOIN Enrollments e ON s.Id = e.StudentId
                LEFT JOIN Classes c ON e.ClassId = c.Id
                WHERE sp.ParentId = @ParentId";
            return Ok(await db.QueryAsync(sql, new { ParentId = parentID }));
        }
    }

        public class CreateDepartmentDto
        {
            public string Name { get; set; } = string.Empty;
        }

        public class UpdateTeacherDto
    {
        public int DepartmentId { get; set; }
        public string EmployeeCode { get; set; } = string.Empty;
    }

    public class AssignSubjectsDto
    {
        public List<int> SubjectIdList { get; set; } = new();
    }

    public class CreateParentDto
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Occupation { get; set; } = string.Empty;
    }

    public class LinkParentStudentDto
    {
        public int StudentId { get; set; }
        public int ParentId { get; set; }
        public string Relationship { get; set; } = "Father";
    }
}
