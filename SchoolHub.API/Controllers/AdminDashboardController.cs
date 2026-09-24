using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;

namespace SchoolHub.API.Controllers
{
    [Route("api/admin/dashboard")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminDashboardController : ControllerBase
    {
        private readonly string _connectionString;

        public AdminDashboardController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        [HttpGet("stats")]
        public async Task<IActionResult> GetDashboardStats()
        {
            using var db = Connection;
            var studentCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Students");
            var teacherCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Teachers");
            var parentCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Parents");
            var classCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Classes");
            var todayAttendanceCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AttendanceRecords ar JOIN AttendanceSessions s ON ar.SessionId = s.Id WHERE s.Date::date = CURRENT_DATE AND ar.Status = 'Present'");
            var upcomingExamsCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ExamSubjects WHERE ExamDate >= CURRENT_TIMESTAMP");
            var announcementCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Announcements");
            var recentActivity = await db.QueryAsync("SELECT Action, Details, CreatedAt FROM AuditLogs ORDER BY CreatedAt DESC LIMIT 5");

            return Ok(new
            {
                TotalStudents = studentCount,
                TotalTeachers = teacherCount,
                TotalParents = parentCount,
                TotalClasses = classCount,
                TodaysAttendance = todayAttendanceCount,
                UpcomingExams = upcomingExamsCount,
                RecentAnnouncementsCount = announcementCount,
                RecentActivity = recentActivity
            });
        }
    }
}
