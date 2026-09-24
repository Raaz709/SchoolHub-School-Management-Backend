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
    public class AnnouncementsController : ControllerBase
    {
        private readonly string _connectionString;

        public AnnouncementsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        [HttpGet]
        public async Task<IActionResult> GetAnnouncements()
        {
            using var db = Connection;
            var announcements = await db.QueryAsync("SELECT * FROM Announcements ORDER BY CreatedAt DESC");
            return Ok(announcements);
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> CreateAnnouncement([FromBody] CreateAnnouncementDto dto)
        {
            using var db = Connection;
            var sql = @"
                INSERT INTO Announcements (Title, Content, TargetRole) 
                VALUES (@Title, @Content, @TargetRole) 
                RETURNING Id;";
            var id = await db.ExecuteScalarAsync<int>(sql, dto);
            return Ok(new { Message = "Announcement published successfully", AnnouncementId = id });
        }
    }

    public class CreateAnnouncementDto
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string TargetRole { get; set; } = "All";
    }
}
