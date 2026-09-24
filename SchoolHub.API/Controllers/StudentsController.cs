using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;

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
                SELECT s.Id, s.RollNumber, s.AdmissionDate, u.Id as UserId, u.Username, u.Email, u.IsActive
                FROM Students s
                JOIN Users u ON s.UserId = u.Id";
            var students = await db.QueryAsync(sql);
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
                // 1. Create User
                var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
                var userSql = @"
                    INSERT INTO Users (Username, Email, PasswordHash, IsActive) 
                    VALUES (@Username, @Email, @PasswordHash, TRUE) 
                    RETURNING Id;";
                var userId = await db.ExecuteScalarAsync<int>(userSql, new { dto.Username, dto.Email, PasswordHash = passwordHash }, transaction);

                // 2. Assign Student Role
                var roleSql = "INSERT INTO UserRoles (UserId, RoleId) SELECT @UserId, Id FROM Roles WHERE Name = 'Student'";
                await db.ExecuteAsync(roleSql, new { UserId = userId }, transaction);

                // 3. Create Student Profile
                var studentSql = @"
                    INSERT INTO Students (UserId, RollNumber, AdmissionDate) 
                    VALUES (@UserId, @RollNumber, @AdmissionDate) 
                    RETURNING Id;";
                var studentId = await db.ExecuteScalarAsync<int>(studentSql, new { UserId = userId, dto.RollNumber, AdmissionDate = dto.AdmissionDate ?? DateTime.UtcNow }, transaction);

                transaction.Commit();
                return Ok(new { Message = "Student created successfully", StudentId = studentId, UserId = userId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { Error = ex.Message });
            }
        }
    }

    public class CreateStudentDto
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string RollNumber { get; set; } = string.Empty;
        public DateTime? AdmissionDate { get; set; }
    }
}
