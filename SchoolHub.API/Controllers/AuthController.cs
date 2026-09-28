using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;
using BCrypt.Net;
using SchoolHub.API.Models;
using SchoolHub.API.Services;

namespace SchoolHub.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ITokenService _tokenService;

        public AuthController(IConfiguration configuration, ITokenService tokenService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=00000";
            _tokenService = tokenService;
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            using var db = Connection;
            var user = await db.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Username = @Username", new { request.Username });

            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return Unauthorized("Invalid username or password.");
            }

            if (!user.IsActive)
            {
                return Unauthorized("Account is deactivated.");
            }

            var accessToken = _tokenService.CreateAccessToken(user);

            return Ok(new AuthResponse
            {
                AccessToken = accessToken,
                Username = user.Username,
                Role = user.Role ?? "Student",
                UserId = user.Id
            });
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            using var db = Connection;
            db.Open();
            using var transaction = db.BeginTransaction();

            try
            {
                if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users WHERE Username = @Username", new { request.Username }, transaction) > 0)
                {
                    return BadRequest("Username is already taken.");
                }

                var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
                var userId = await db.ExecuteScalarAsync<int>(@"
                    INSERT INTO Users (Username, Email, PasswordHash, Role, IsActive) 
                    VALUES (@Username, @Email, @PasswordHash, @Role, TRUE) 
                    RETURNING Id;", new { request.Username, request.Email, PasswordHash = passwordHash, Role = request.Role }, transaction);

                if (request.Role == "Student")
                {
                    await db.ExecuteAsync(@"
                        INSERT INTO Students (UserId, RollNumber, AdmissionDate) 
                        VALUES (@UserId, @RollNumber, @AdmissionDate)",
                        new { UserId = userId, request.RollNumber, AdmissionDate = request.AdmissionDate ?? DateTime.UtcNow }, transaction);
                }
                else if (request.Role == "Teacher")
                {
                    await db.ExecuteAsync(@"
                        INSERT INTO Teachers (UserId, DepartmentId, EmployeeCode, HireDate) 
                        VALUES (@UserId, @DepartmentId, @EmployeeCode, @HireDate)",
                        new { UserId = userId, request.DepartmentId, request.EmployeeCode, HireDate = request.HireDate ?? DateTime.UtcNow }, transaction);
                }
                else if (request.Role == "Parent")
                {
                    await db.ExecuteAsync(@"
                        INSERT INTO Parents (UserId, Occupation) 
                        VALUES (@UserId, @Occupation)",
                        new { UserId = userId, request.Occupation }, transaction);
                }

                var user = await db.QueryFirstOrDefaultAsync<User>("SELECT * FROM Users WHERE Id = @Id", new { Id = userId }, transaction);
                
                transaction.Commit();

                var accessToken = _tokenService.CreateAccessToken(user);

                return Ok(new AuthResponse
                {
                    AccessToken = accessToken,
                    Username = user.Username,
                    Role = user.Role ?? "Student",
                    UserId = user.Id
                });
            }
            catch (Exception ex)
            {
                if (db.State == ConnectionState.Open)
                {
                    transaction.Rollback();
                }
                return StatusCode(500, new { Error = ex.Message });
            }
        }
    }

    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class RegisterRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = "Student";
        public string Department { get; set; } = string.Empty;
        public int? ClassRoomId { get; set; }
        public string RollNumber { get; set; } = string.Empty;
        public int? DepartmentId { get; set; }
        public string EmployeeCode { get; set; } = string.Empty;
        public DateTime? HireDate { get; set; }
        public string Occupation { get; set; } = string.Empty;
        public DateTime? AdmissionDate { get; set; }
    }

    public class AuthResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int UserId { get; set; }
    }
}