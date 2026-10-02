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
    public class AnnouncementsController : ControllerBase
    {
        private readonly string _connectionString;

        public AnnouncementsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        private int CurrentUserId()
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(raw, out var id) ? id : 0;
        }

        private static async Task<int?> StudentIdForAsync(IDbConnection db, int userId)
            => await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM Students WHERE UserId = @u", new { u = userId });

        /// <summary>The audiences the compose form offers and the check accepts.</summary>
        private static readonly string[] TargetRoles = { "All", "Admin", "Teacher", "Student", "Parent" };

        /// <summary>
        /// List projection. `ClassName` and `AuthorName` are joined in so the feed
        /// can name the class and who posted it without a second round trip.
        /// </summary>
        private const string AnnouncementSelect = @"
            SELECT a.Id, a.Title, a.Content, a.TargetRole, a.ClassId, a.CreatedAt,
                   c.Name AS ClassName,
                   u.Username AS AuthorName
            FROM Announcements a
            LEFT JOIN Classes c ON c.Id = a.ClassId
            LEFT JOIN Users u ON u.Id = a.AuthorId";

        /// <summary>
        /// The audience predicate for the caller. Admin and Teacher see every
        /// notice; a Student and a Parent see the ones addressed to them, narrowed
        /// to their own class (or their child's) when the notice names one.
        /// Callers pass all three parameters and the predicate uses the relevant one.
        /// </summary>
        private string ScopeSql()
        {
            if (User.IsInRole("Admin") || User.IsInRole("Teacher")) return "TRUE";

            if (User.IsInRole("Student"))
                return @"a.TargetRole IN ('All', 'Student')
                         AND (a.ClassId IS NULL
                              OR a.ClassId = (SELECT e.ClassId FROM Enrollments e WHERE e.StudentId = @StudentId))";

            return @"a.TargetRole IN ('All', 'Parent')
                     AND (a.ClassId IS NULL
                          OR a.ClassId IN (SELECT e.ClassId FROM Enrollments e
                                           JOIN StudentParents sp ON sp.StudentId = e.StudentId
                                           JOIN Parents p ON p.Id = sp.ParentId
                                           WHERE p.UserId = @ParentUserId))";
        }

        private async Task<object> ScopeParamsAsync(IDbConnection db, int? id)
        {
            var studentId = User.IsInRole("Student")
                ? await StudentIdForAsync(db, CurrentUserId())
                : null;
            return new { Id = id, StudentId = studentId ?? 0, ParentUserId = CurrentUserId() };
        }

        // --- READS ---

        /// <summary>
        /// Every notice the caller is entitled to see. Staff see the lot; a
        /// learner sees only what targets them, and only their own class's.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAnnouncements()
        {
            using var db = Connection;
            var parameters = await ScopeParamsAsync(db, null);
            var rows = await db.QueryAsync(
                AnnouncementSelect + $" WHERE {ScopeSql()} ORDER BY a.CreatedAt DESC", parameters);
            return Ok(rows);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetAnnouncement(int id)
        {
            using var db = Connection;
            var parameters = await ScopeParamsAsync(db, id);
            var row = await db.QuerySingleOrDefaultAsync(
                AnnouncementSelect + $" WHERE a.Id = @Id AND ({ScopeSql()})", parameters);
            if (row == null) return NotFound(new { Message = "Announcement not found." });
            return Ok(row);
        }

        // --- WRITES (staff) ---

        [HttpPost]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> CreateAnnouncement([FromBody] SaveAnnouncementDto dto)
        {
            using var db = Connection;
            var error = Validate(dto, out var title, out var content, out var targetRole);
            if (error != null) return BadRequest(new { Message = error });

            var classId = dto.ClassId is > 0 ? dto.ClassId : null;
            if (classId.HasValue && !await ClassExistsAsync(db, classId.Value))
                return BadRequest(new { Message = "That class does not exist." });

            var id = await db.ExecuteScalarAsync<int>(
                @"INSERT INTO Announcements (Title, Content, TargetRole, ClassId, AuthorId)
                  VALUES (@Title, @Content, @TargetRole, @ClassId, @AuthorId)
                  RETURNING Id",
                new { Title = title, Content = content, TargetRole = targetRole,
                      ClassId = classId, AuthorId = CurrentUserId() });

            return Ok(new { Message = "Announcement published successfully", AnnouncementId = id });
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> UpdateAnnouncement(int id, [FromBody] SaveAnnouncementDto dto)
        {
            using var db = Connection;
            if (!await ExistsAsync(db, id))
                return NotFound(new { Message = "Announcement not found." });
            if (!await CanManageAsync(db, id)) return Forbid();

            var error = Validate(dto, out var title, out var content, out var targetRole);
            if (error != null) return BadRequest(new { Message = error });

            var classId = dto.ClassId is > 0 ? dto.ClassId : null;
            if (classId.HasValue && !await ClassExistsAsync(db, classId.Value))
                return BadRequest(new { Message = "That class does not exist." });

            await db.ExecuteAsync(
                @"UPDATE Announcements
                  SET Title = @Title, Content = @Content, TargetRole = @TargetRole, ClassId = @ClassId
                  WHERE Id = @Id",
                new { Title = title, Content = content, TargetRole = targetRole, ClassId = classId, Id = id });

            return Ok(new { Message = "Announcement updated successfully" });
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> DeleteAnnouncement(int id)
        {
            using var db = Connection;
            if (!await ExistsAsync(db, id))
                return NotFound(new { Message = "Announcement not found." });
            if (!await CanManageAsync(db, id)) return Forbid();

            await db.ExecuteAsync("DELETE FROM Announcements WHERE Id = @Id", new { Id = id });
            return Ok(new { Message = "Announcement deleted successfully" });
        }

        // --- HELPERS ---

        private static string? Validate(
            SaveAnnouncementDto dto, out string title, out string content, out string targetRole)
        {
            title = (dto.Title ?? string.Empty).Trim();
            content = (dto.Content ?? string.Empty).Trim();
            targetRole = string.IsNullOrWhiteSpace(dto.TargetRole) ? "All" : dto.TargetRole.Trim();

            if (title.Length == 0) return "Announcement title is required.";
            if (title.Length > 255) return "Announcement title must be 255 characters or fewer.";
            if (content.Length == 0) return "Announcement content is required.";
            if (!TargetRoles.Contains(targetRole))
                return "Target role must be one of All, Admin, Teacher, Student or Parent.";
            return null;
        }

        private static async Task<bool> ExistsAsync(IDbConnection db, int id)
            => await db.ExecuteScalarAsync<bool>(
                "SELECT EXISTS (SELECT 1 FROM Announcements WHERE Id = @Id)", new { Id = id });

        private static async Task<bool> ClassExistsAsync(IDbConnection db, int classId)
            => await db.ExecuteScalarAsync<bool>(
                "SELECT EXISTS (SELECT 1 FROM Classes WHERE Id = @Id)", new { Id = classId });

        /// <summary>Admin may manage any notice; a teacher only their own.</summary>
        private async Task<bool> CanManageAsync(IDbConnection db, int id)
        {
            if (User.IsInRole("Admin")) return true;
            var authorId = await db.ExecuteScalarAsync<int?>(
                "SELECT AuthorId FROM Announcements WHERE Id = @Id", new { Id = id });
            return authorId.HasValue && authorId.Value == CurrentUserId();
        }
    }

    public class SaveAnnouncementDto
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string TargetRole { get; set; } = "All";
        public int? ClassId { get; set; }
    }
}
