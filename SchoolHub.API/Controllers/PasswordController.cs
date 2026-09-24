using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;
using System.Security.Claims;

namespace SchoolHub.API.Controllers
{
    [Route("api/auth/password")]
    [ApiController]
    public class PasswordController : ControllerBase
    {
        private readonly string _connectionString;

        public PasswordController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        [HttpPost("forgot")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            using var db = Connection;
            var userId = await db.ExecuteScalarAsync<int?>("SELECT Id FROM Users WHERE Email = @Email", new { dto.Email });
            if (!userId.HasValue) return Ok(new { Message = "If email exists, reset token sent." });

            var token = Guid.NewGuid().ToString();
            var expires = DateTime.UtcNow.AddHours(1);

            await db.ExecuteAsync(
                "INSERT INTO PasswordResetTokens (UserId, Token, Expires, IsUsed) VALUES (@UserId, @Token, @Expires, FALSE)",
                new { UserId = userId.Value, Token = token, Expires = expires });

            // In production, send email with token. For portfolio/demo, return token.
            return Ok(new { Message = "Password reset token generated", ResetToken = token });
        }

        [HttpPost("reset")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            using var db = Connection;
            var tokenRecord = await db.QueryFirstOrDefaultAsync<PasswordResetTokenModel>(
                "SELECT * FROM PasswordResetTokens WHERE Token = @Token AND IsUsed = FALSE AND Expires >= CURRENT_TIMESTAMP",
                new { dto.Token });

            if (tokenRecord == null) return BadRequest("Invalid or expired reset token.");

            var newHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await db.ExecuteAsync("UPDATE Users SET PasswordHash = @PasswordHash WHERE Id = @UserId", new { PasswordHash = newHash, UserId = tokenRecord.UserId });
            await db.ExecuteAsync("UPDATE PasswordResetTokens SET IsUsed = TRUE WHERE Id = @Id", new { Id = tokenRecord.Id });

            return Ok(new { Message = "Password reset successfully" });
        }

        [HttpPost("change")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            using var db = Connection;
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(userIdStr, out int userId);

            var currentHash = await db.ExecuteScalarAsync<string>("SELECT PasswordHash FROM Users WHERE Id = @Id", new { Id = userId });
            if (currentHash == null || !BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, currentHash))
            {
                return BadRequest("Incorrect current password.");
            }

            var newHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await db.ExecuteAsync("UPDATE Users SET PasswordHash = @PasswordHash WHERE Id = @Id", new { PasswordHash = newHash, Id = userId });

            return Ok(new { Message = "Password changed successfully" });
        }
    }

    public class ForgotPasswordDto
    {
        public string Email { get; set; } = string.Empty;
    }

    public class ResetPasswordDto
    {
        public string Token { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class ChangePasswordDto
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class PasswordResetTokenModel
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Token { get; set; } = string.Empty;
        public DateTime Expires { get; set; }
        public bool IsUsed { get; set; }
    }
}
