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
    public class ProfileController : ControllerBase
    {
        private readonly string _connectionString;

        public ProfileController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        [HttpGet]
        public async Task<IActionResult> GetProfile()
        {
            using var db = Connection;
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(userIdStr, out int userId);

            var user = await db.QueryFirstOrDefaultAsync(@"
                SELECT Id, Username, Email, ProfilePictureUrl, IsActive, CreatedAt 
                FROM Users WHERE Id = @Id", new { Id = userId });

            if (user == null) return NotFound();
            return Ok(user);
        }

        [HttpPut]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
        {
            using var db = Connection;
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(userIdStr, out int userId);

            var sql = "UPDATE Users SET Username = @Username, Email = @Email WHERE Id = @Id";
            await db.ExecuteAsync(sql, new { dto.Username, dto.Email, Id = userId });

            return Ok(new { Message = "Profile updated successfully" });
        }

        [HttpPost("picture")]
        public async Task<IActionResult> UpdateProfilePicture([FromBody] UpdateProfilePictureDto dto)
        {
            using var db = Connection;
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(userIdStr, out int userId);

            var sql = "UPDATE Users SET ProfilePictureUrl = @ProfilePictureUrl WHERE Id = @Id";
            await db.ExecuteAsync(sql, new { dto.ProfilePictureUrl, Id = userId });

            return Ok(new { Message = "Profile picture updated successfully" });
        }
    }

    public class UpdateProfileDto
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class UpdateProfilePictureDto
    {
        public string ProfilePictureUrl { get; set; } = string.Empty;
    }
}
