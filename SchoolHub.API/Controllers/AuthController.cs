using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;
using BCrypt.Net;
using SchoolHub.API.Models;
using SchoolHub.API.DTOs;
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

            // Opportunistic housekeeping so the table cannot grow without bound.
            await PurgeStaleRefreshTokensAsync(db);

            var accessToken = _tokenService.CreateAccessToken(user);
            var refresh = _tokenService.GenerateRefreshToken(user.Id);

            await db.ExecuteAsync(
                "INSERT INTO RefreshTokens (UserId, Token, Expires) VALUES (@UserId, @Token, @Expires)",
                new { UserId = user.Id, Token = refresh.Token, Expires = refresh.Expires });

            return Ok(new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = refresh.Token,
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

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
        {
            using var db = Connection;

            // Select the scalar id directly: an unquoted "UserId" column folds to
            // lowercase in Postgres, so reading it off a dynamic row yields null.
            var storedId = await db.QueryFirstOrDefaultAsync<int?>(
                "SELECT Id FROM RefreshTokens WHERE Token = @RefreshToken AND IsRevoked = FALSE AND Expires > CURRENT_TIMESTAMP",
                new { request.RefreshToken });

            if (!storedId.HasValue) return Unauthorized("Invalid or expired refresh token.");

            // Rotate: revoke the token that was just used
            await db.ExecuteAsync("UPDATE RefreshTokens SET IsRevoked = TRUE WHERE Id = @Id",
                new { Id = storedId.Value });

            var ownerId = await db.QueryFirstOrDefaultAsync<int?>(
                "SELECT UserId FROM RefreshTokens WHERE Id = @Id",
                new { Id = storedId.Value });

            var user = await db.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Id = @Id", new { Id = ownerId });

            if (user == null || !user.IsActive) return Unauthorized("Account is unavailable.");

            var accessToken = _tokenService.CreateAccessToken(user);
            var newRefresh = _tokenService.GenerateRefreshToken(user.Id);

            await db.ExecuteAsync(@"INSERT INTO RefreshTokens (UserId, Token, Expires) VALUES (@UserId, @Token, @Expires)",
                new { UserId = user.Id, Token = newRefresh.Token, Expires = newRefresh.Expires });

            return Ok(new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = newRefresh.Token,
                Username = user.Username,
                Role = user.Role ?? "Student",
                UserId = user.Id
            });
        }

        /// <summary>
        /// Revokes the caller's refresh token so the session cannot be renewed
        /// after logout. Safe to call repeatedly and with an unknown token.
        /// </summary>
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.RefreshToken))
            {
                return Ok(new { Message = "Logged out" });
            }

            try
            {
                using var db = Connection;
                await db.ExecuteAsync(
                    "UPDATE RefreshTokens SET IsRevoked = TRUE WHERE Token = @RefreshToken",
                    new { request.RefreshToken });

                await PurgeStaleRefreshTokensAsync(db);
            }
            catch
            {
                // Logout is best-effort: the client clears its own tokens
                // regardless, so a failure here must not surface to the user.
            }

            return Ok(new { Message = "Logged out" });
        }

        /// <summary>
        /// Deletes refresh tokens that are expired or were revoked more than a
        /// day ago (revoked rows are briefly retained for audit).
        /// Identifiers are deliberately unquoted: the table and its columns are
        /// stored lowercase by PostgreSQL, so quoting them would break matching.
        /// </summary>
        private static async Task PurgeStaleRefreshTokensAsync(IDbConnection db)
        {
            await db.ExecuteAsync(
                @"DELETE FROM RefreshTokens
                  WHERE Expires < CURRENT_TIMESTAMP
                     OR (IsRevoked = TRUE
                         AND COALESCE(Created, Expires) < CURRENT_TIMESTAMP - INTERVAL '1 day')");
        }

        public class LogoutRequest
        {
            public string RefreshToken { get; set; } = string.Empty;
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
}