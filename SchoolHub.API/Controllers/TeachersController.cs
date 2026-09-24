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
    public class TeachersController : ControllerBase
    {
        private readonly string _connectionString;

        public TeachersController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        [HttpGet]
        public async Task<IActionResult> GetAllTeachers()
        {
            using var db = Connection;
            var sql = @"
                SELECT t.Id, t.EmployeeCode, t.HireDate, d.Name as DepartmentName, u.Id as UserId, u.Username, u.Email
                FROM Teachers t
                JOIN Users u ON t.UserId = u.Id
                LEFT JOIN Departments d ON t.DepartmentId = d.Id";
            var teachers = await db.QueryAsync(sql);
            return Ok(teachers);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateTeacher([FromBody] CreateTeacherDto dto)
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

                var roleSql = "INSERT INTO UserRoles (UserId, RoleId) SELECT @UserId, Id FROM Roles WHERE Name = 'Teacher'";
                await db.ExecuteAsync(roleSql, new { UserId = userId }, transaction);

                var teacherSql = @"
                    INSERT INTO Teachers (UserId, DepartmentId, EmployeeCode, HireDate) 
                    VALUES (@UserId, @DepartmentId, @EmployeeCode, @HireDate) 
                    RETURNING Id;";
                var teacherId = await db.ExecuteScalarAsync<int>(teacherSql, new { UserId = userId, dto.DepartmentId, dto.EmployeeCode, HireDate = dto.HireDate ?? DateTime.UtcNow }, transaction);

                transaction.Commit();
                return Ok(new { Message = "Teacher created successfully", TeacherId = teacherId, UserId = userId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { Error = ex.Message });
            }
        }
    }

    public class CreateTeacherDto
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string EmployeeCode { get; set; } = string.Empty;
        public int? DepartmentId { get; set; }
        public DateTime? HireDate { get; set; }
    }
}
