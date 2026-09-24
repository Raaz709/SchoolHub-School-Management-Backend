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
    public class AssignmentsController : ControllerBase
    {
        private readonly string _connectionString;

        public AssignmentsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        [HttpGet]
        public async Task<IActionResult> GetAssignments()
        {
            using var db = Connection;
            var sql = @"
                SELECT a.Id, a.Title, a.Description, a.DueDate, a.MaxScore, s.Name as SubjectName
                FROM Assignments a
                JOIN Subjects s ON a.SubjectId = s.Id";
            var assignments = await db.QueryAsync(sql);
            return Ok(assignments);
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> CreateAssignment([FromBody] CreateAssignmentDto dto)
        {
            using var db = Connection;
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(userIdStr, out int userId);

            int? teacherId = await db.ExecuteScalarAsync<int?>("SELECT Id FROM Teachers WHERE UserId = @UserId", new { UserId = userId });

            var sql = @"
                INSERT INTO Assignments (SubjectId, TeacherId, Title, Description, DueDate, MaxScore) 
                VALUES (@SubjectId, @TeacherId, @Title, @Description, @DueDate, @MaxScore) 
                RETURNING Id;";
            var id = await db.ExecuteScalarAsync<int>(sql, new {
                dto.SubjectId,
                TeacherId = teacherId,
                dto.Title,
                dto.Description,
                dto.DueDate,
                dto.MaxScore
            });

            return Ok(new { Message = "Assignment created successfully", AssignmentId = id });
        }

        [HttpPost("submit")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> SubmitAssignment([FromBody] SubmitAssignmentDto dto)
        {
            using var db = Connection;
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(userIdStr, out int userId);

            int studentId = await db.ExecuteScalarAsync<int>("SELECT Id FROM Students WHERE UserId = @UserId", new { UserId = userId });

            // Check if submission already exists, update or insert
            var existingId = await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM AssignmentSubmissions WHERE AssignmentId = @AssignmentId AND StudentId = @StudentId",
                new { dto.AssignmentId, StudentId = studentId });

            if (existingId.HasValue)
            {
                var updateSql = "UPDATE AssignmentSubmissions SET FilePath = @FilePath, SubmittedAt = CURRENT_TIMESTAMP WHERE Id = @Id";
                await db.ExecuteAsync(updateSql, new { FilePath = dto.FilePath, Id = existingId.Value });
                return Ok(new { Message = "Assignment submission updated successfully" });
            }
            else
            {
                var insertSql = @"
                    INSERT INTO AssignmentSubmissions (AssignmentId, StudentId, FilePath) 
                    VALUES (@AssignmentId, @StudentId, @FilePath) 
                    RETURNING Id;";
                var subId = await db.ExecuteScalarAsync<int>(insertSql, new { dto.AssignmentId, StudentId = studentId, FilePath = dto.FilePath });
                return Ok(new { Message = "Assignment submitted successfully", SubmissionId = subId });
            }
        }
    }

    public class CreateAssignmentDto
    {
        public int SubjectId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime DueDate { get; set; }
        public decimal MaxScore { get; set; }
    }

    public class SubmitAssignmentDto
    {
        public int AssignmentId { get; set; }
        public string FilePath { get; set; } = string.Empty;
    }
}
