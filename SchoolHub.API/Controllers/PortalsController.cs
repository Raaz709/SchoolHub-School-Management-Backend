using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;
using System.Security.Claims;

namespace SchoolHub.API.Controllers
{
    [Route("api/portals")]
    [ApiController]
    [Authorize]
    public class PortalsController : ControllerBase
    {
        private readonly string _connectionString;

        public PortalsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        [HttpGet("student/dashboard")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> GetStudentDashboard()
        {
            using var db = Connection;
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(userIdStr, out int userId);

            int studentId = await db.ExecuteScalarAsync<int>("SELECT Id FROM Students WHERE UserId = @UserId", new { UserId = userId });

            // Attendance Percentage
            var attendanceStats = await db.QueryFirstOrDefaultAsync(@"
                SELECT 
                    COUNT(CASE WHEN Status = 'Present' THEN 1 END)::decimal / NULLIF(COUNT(*), 0) * 100 as AttendancePercentage
                FROM AttendanceRecords WHERE StudentId = @StudentId", new { StudentId = studentId });

            // Pending Assignments
            var pendingAssignments = await db.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*) FROM Assignments a
                WHERE a.DueDate >= CURRENT_TIMESTAMP 
                  AND NOT EXISTS (SELECT 1 FROM AssignmentSubmissions sub WHERE sub.AssignmentId = a.Id AND sub.StudentId = @StudentId)", 
                new { StudentId = studentId });

            // Upcoming Exam
            var upcomingExam = await db.QueryFirstOrDefaultAsync(@"
                SELECT ex.Title, es.ExamDate FROM ExamSubjects es
                JOIN Exams ex ON es.ExamId = ex.Id
                WHERE es.ExamDate >= CURRENT_TIMESTAMP ORDER BY es.ExamDate ASC LIMIT 1", new { });

            // Unread Notifications
            var unreadNotifs = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Notifications WHERE UserId = @UserId AND IsRead = FALSE", new { UserId = userId });

            return Ok(new
            {
                AttendancePercentage = attendanceStats?.AttendancePercentage ?? 100.0,
                PendingAssignments = pendingAssignments,
                UpcomingExam = upcomingExam?.Title ?? "None scheduled",
                UnreadNotifications = unreadNotifs
            });
        }

        [HttpGet("parent/children")]
        [Authorize(Roles = "Parent")]
        public async Task<IActionResult> GetParentChildren()
        {
            using var db = Connection;
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(userIdStr, out int userId);

            int parentId = await db.ExecuteScalarAsync<int>("SELECT Id FROM Parents WHERE UserId = @UserId", new { UserId = userId });

            var sql = @"
                SELECT s.Id as StudentId, s.RollNumber, u.Username as StudentName, c.Name as ClassName, sec.Name as SectionName
                FROM StudentParents sp
                JOIN Students s ON sp.StudentId = s.Id
                JOIN Users u ON s.UserId = u.Id
                LEFT JOIN Enrollments e ON s.Id = e.StudentId
                LEFT JOIN Classes c ON e.ClassId = c.Id
                LEFT JOIN Sections sec ON e.SectionId = sec.Id
                WHERE sp.ParentId = @ParentId";
            return Ok(await db.QueryAsync(sql, new { ParentId = parentId }));
        }
    }
}
