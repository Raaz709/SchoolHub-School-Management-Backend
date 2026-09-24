using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;
using System.Security.Claims;

namespace SchoolHub.API.Controllers
{
    [Route("api/teacher")]
    [ApiController]
    [Authorize(Roles = "Teacher,Admin")]
    public class TeacherPortalController : ControllerBase
    {
        private readonly string _connectionString;

        public TeacherPortalController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        [HttpGet("classes")]
        public async Task<IActionResult> GetTeacherClasses()
        {
            using var db = Connection;
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(userIdStr, out int userId);

            var sql = @"
                SELECT DISTINCT c.Id, c.Name, sec.Name as SectionName
                FROM Teachers t
                JOIN TimetableEntries tt ON t.Id = tt.TeacherId
                JOIN Classes c ON tt.ClassId = c.Id
                JOIN Sections sec ON tt.SectionId = sec.Id
                WHERE t.UserId = @UserId";
            return Ok(await db.QueryAsync(sql, new { UserId = userId }));
        }

        [HttpGet("subjects")]
        public async Task<IActionResult> GetTeacherSubjects()
        {
            using var db = Connection;
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(userIdStr, out int userId);

            var sql = @"
                SELECT s.Id, s.Name, s.Code
                FROM Teachers t
                JOIN Subjects s ON t.Id = s.TeacherId
                WHERE t.UserId = @UserId";
            return Ok(await db.QueryAsync(sql, new { UserId = userId }));
        }

        [HttpGet("attendance-history")]
        public async Task<IActionResult> GetAttendanceHistory([FromQuery] int classId, [FromQuery] int sectionId)
        {
            using var db = Connection;
            var sql = @"
                SELECT s.Id as SessionId, s.Date, c.Name as ClassName, sec.Name as SectionName,
                       COUNT(ar.Id) as TotalStudentsMarked
                FROM AttendanceSessions s
                JOIN Classes c ON s.ClassId = c.Id
                JOIN Sections sec ON s.SectionId = sec.Id
                LEFT JOIN AttendanceRecords ar ON s.Id = ar.SessionId
                WHERE s.ClassId = @ClassId AND s.SectionId = @SectionId
                GROUP BY s.Id, s.Date, c.Name, sec.Name
                ORDER BY s.Date DESC";
            return Ok(await db.QueryAsync(sql, new { ClassId = classId, SectionId = sectionId }));
        }
    }
}
