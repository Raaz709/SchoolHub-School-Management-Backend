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

        private int CurrentUserId()
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(raw, out var id) ? id : 0;
        }

        private static async Task<int?> TeacherIdForAsync(IDbConnection db, int userId)
            => await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM Teachers WHERE UserId = @u", new { u = userId });

        private static async Task<int?> StudentIdForAsync(IDbConnection db, int userId)
            => await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM Students WHERE UserId = @u", new { u = userId });

        private static async Task<int?> ClassIdForStudentAsync(IDbConnection db, int studentId)
            => await db.ExecuteScalarAsync<int?>(
                "SELECT ClassId FROM Enrollments WHERE StudentId = @s", new { s = studentId });

        /// <summary>The columns the write paths need to check ownership and bounds.</summary>
        private sealed class AssignmentRow
        {
            public int Id { get; set; }
            public int SubjectId { get; set; }
            public int? TeacherId { get; set; }
            public decimal? MaxScore { get; set; }
        }

        private sealed class SubmissionRow
        {
            public int Id { get; set; }
            public int AssignmentId { get; set; }
        }

        /// <summary>
        /// Shared projection for the list and the single read. A learner passes
        /// their student id so the submission columns fill in; staff pass 0, so
        /// the left join matches nothing and those columns stay null.
        /// </summary>
        private const string AssignmentSelect = @"
            SELECT a.Id, a.SubjectId, a.Title, a.Description, a.DueDate, a.MaxScore, a.AttachmentUrl,
                   s.Name AS SubjectName,
                   u.Username AS TeacherName,
                   (SELECT COUNT(*) FROM AssignmentSubmissions x WHERE x.AssignmentId = a.Id) AS SubmissionCount,
                   sub.Id AS MySubmissionId, sub.SubmittedAt AS MySubmittedAt,
                   sub.Score AS MyScore, sub.Feedback AS MyFeedback, sub.FilePath AS MyFilePath
            FROM Assignments a
            JOIN Subjects s ON s.Id = a.SubjectId
            LEFT JOIN Teachers t ON t.Id = a.TeacherId
            LEFT JOIN Users u ON u.Id = t.UserId
            LEFT JOIN AssignmentSubmissions sub ON sub.AssignmentId = a.Id AND sub.StudentId = @StudentId";

        // --- READS ---

        /// <summary>
        /// Staff see every assignment. A Student sees only the assignments whose
        /// subject their class offers, carrying their own submission back.
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Admin,Teacher,Student")]
        public async Task<IActionResult> GetAssignments()
        {
            using var db = Connection;

            if (User.IsInRole("Student"))
            {
                var studentId = await StudentIdForAsync(db, CurrentUserId());
                var classId = studentId.HasValue
                    ? await ClassIdForStudentAsync(db, studentId.Value)
                    : null;

                // No enrolment means nothing is set for this learner yet.
                if (!classId.HasValue) return Ok(Array.Empty<object>());

                var mine = await db.QueryAsync(
                    AssignmentSelect + @"
                    WHERE a.SubjectId IN (SELECT SubjectId FROM ClassSubjects WHERE ClassId = @ClassId)
                    ORDER BY a.DueDate ASC",
                    new { StudentId = studentId.Value, ClassId = classId.Value });
                return Ok(mine);
            }

            var all = await db.QueryAsync(
                AssignmentSelect + " ORDER BY a.DueDate ASC",
                new { StudentId = 0 });
            return Ok(all);
        }

        [HttpGet("{id}")]
        [Authorize(Roles = "Admin,Teacher,Student")]
        public async Task<IActionResult> GetAssignment(int id)
        {
            using var db = Connection;
            var isStudent = User.IsInRole("Student");
            var studentId = isStudent ? await StudentIdForAsync(db, CurrentUserId()) : null;

            var row = await db.QuerySingleOrDefaultAsync(
                AssignmentSelect + " WHERE a.Id = @Id",
                new { Id = id, StudentId = studentId ?? 0 });
            if (row == null) return NotFound(new { Message = "Assignment not found." });

            // A learner reaching for an assignment their class does not offer
            // gets the same 404 as one that does not exist.
            if (isStudent)
            {
                var classId = studentId.HasValue
                    ? await ClassIdForStudentAsync(db, studentId.Value)
                    : null;
                if (!classId.HasValue) return NotFound(new { Message = "Assignment not found." });

                var subjectId = await db.ExecuteScalarAsync<int>(
                    "SELECT SubjectId FROM Assignments WHERE Id = @Id", new { Id = id });
                var offered = await db.ExecuteScalarAsync<bool>(
                    "SELECT EXISTS (SELECT 1 FROM ClassSubjects WHERE ClassId = @c AND SubjectId = @s)",
                    new { c = classId.Value, s = subjectId });
                if (!offered) return NotFound(new { Message = "Assignment not found." });
            }

            return Ok(row);
        }

        // --- WRITES (staff) ---

        [HttpPost]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> CreateAssignment([FromBody] SaveAssignmentDto dto)
        {
            using var db = Connection;
            var error = ValidateAssignment(dto, out var title, out var description, out var attachment);
            if (error != null) return BadRequest(new { Message = error });

            var subjectExists = await db.ExecuteScalarAsync<bool>(
                "SELECT EXISTS (SELECT 1 FROM Subjects WHERE Id = @Id)", new { Id = dto.SubjectId });
            if (!subjectExists) return BadRequest(new { Message = "That subject does not exist." });

            // A teacher owns what they create; an Admin's assignment has no owner
            // and stays editable only by an Admin.
            var teacherId = await TeacherIdForAsync(db, CurrentUserId());

            var id = await db.ExecuteScalarAsync<int>(
                @"INSERT INTO Assignments (SubjectId, TeacherId, Title, Description, DueDate, MaxScore, AttachmentUrl)
                  VALUES (@SubjectId, @TeacherId, @Title, @Description, @DueDate, @MaxScore, @AttachmentUrl)
                  RETURNING Id",
                new { dto.SubjectId, TeacherId = teacherId, Title = title,
                      Description = description, dto.DueDate, dto.MaxScore, AttachmentUrl = attachment });

            return Ok(new { Message = "Assignment created successfully", AssignmentId = id });
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> UpdateAssignment(int id, [FromBody] SaveAssignmentDto dto)
        {
            using var db = Connection;
            var error = ValidateAssignment(dto, out var title, out var description, out var attachment);
            if (error != null) return BadRequest(new { Message = error });

            var assignment = await db.QuerySingleOrDefaultAsync<AssignmentRow>(
                "SELECT Id, SubjectId, TeacherId, MaxScore FROM Assignments WHERE Id = @Id", new { Id = id });
            if (assignment == null) return NotFound(new { Message = "Assignment not found." });
            if (!await CanManageAsync(db, assignment, CurrentUserId(), User.IsInRole("Admin")))
                return Forbid();

            var subjectExists = await db.ExecuteScalarAsync<bool>(
                "SELECT EXISTS (SELECT 1 FROM Subjects WHERE Id = @Id)", new { Id = dto.SubjectId });
            if (!subjectExists) return BadRequest(new { Message = "That subject does not exist." });

            await db.ExecuteAsync(
                @"UPDATE Assignments
                  SET SubjectId = @SubjectId, Title = @Title, Description = @Description,
                      DueDate = @DueDate, MaxScore = @MaxScore, AttachmentUrl = @AttachmentUrl
                  WHERE Id = @Id",
                new { dto.SubjectId, Title = title, Description = description, dto.DueDate,
                      dto.MaxScore, AttachmentUrl = attachment, Id = id });

            return Ok(new { Message = "Assignment updated successfully" });
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> DeleteAssignment(int id)
        {
            using var db = Connection;
            var assignment = await db.QuerySingleOrDefaultAsync<AssignmentRow>(
                "SELECT Id, SubjectId, TeacherId, MaxScore FROM Assignments WHERE Id = @Id", new { Id = id });
            if (assignment == null) return NotFound(new { Message = "Assignment not found." });
            if (!await CanManageAsync(db, assignment, CurrentUserId(), User.IsInRole("Admin")))
                return Forbid();

            await db.ExecuteAsync("DELETE FROM Assignments WHERE Id = @Id", new { Id = id });
            return Ok(new { Message = "Assignment deleted successfully" });
        }

        // --- SUBMISSIONS ---

        [HttpGet("{id}/submissions")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> GetSubmissions(int id)
        {
            using var db = Connection;
            var assignment = await db.QuerySingleOrDefaultAsync<AssignmentRow>(
                "SELECT Id, SubjectId, TeacherId, MaxScore FROM Assignments WHERE Id = @Id", new { Id = id });
            if (assignment == null) return NotFound(new { Message = "Assignment not found." });
            if (!await CanManageAsync(db, assignment, CurrentUserId(), User.IsInRole("Admin")))
                return Forbid();

            var rows = await db.QueryAsync(
                @"SELECT sub.Id, sub.StudentId, st.RollNumber, u.Username AS StudentName,
                         sub.FilePath, sub.SubmittedAt, sub.Score, sub.Feedback
                  FROM AssignmentSubmissions sub
                  JOIN Students st ON st.Id = sub.StudentId
                  JOIN Users u ON u.Id = st.UserId
                  WHERE sub.AssignmentId = @Id
                  ORDER BY sub.SubmittedAt ASC",
                new { Id = id });
            return Ok(rows);
        }

        /// <summary>Set or clear a submission's score and feedback.</summary>
        [HttpPut("submissions/{submissionId}")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> GradeSubmission(int submissionId, [FromBody] GradeSubmissionDto dto)
        {
            using var db = Connection;
            var submission = await db.QuerySingleOrDefaultAsync<SubmissionRow>(
                "SELECT Id, AssignmentId FROM AssignmentSubmissions WHERE Id = @Id", new { Id = submissionId });
            if (submission == null) return NotFound(new { Message = "Submission not found." });

            var assignment = await db.QuerySingleAsync<AssignmentRow>(
                "SELECT Id, SubjectId, TeacherId, MaxScore FROM Assignments WHERE Id = @Id",
                new { Id = submission.AssignmentId });
            if (!await CanManageAsync(db, assignment, CurrentUserId(), User.IsInRole("Admin")))
                return Forbid();

            if (dto.Score is < 0)
                return BadRequest(new { Message = "Score cannot be negative." });
            if (dto.Score.HasValue && assignment.MaxScore.HasValue && dto.Score > assignment.MaxScore)
                return BadRequest(new { Message = $"Score cannot exceed {assignment.MaxScore}." });

            await db.ExecuteAsync(
                "UPDATE AssignmentSubmissions SET Score = @Score, Feedback = @Feedback WHERE Id = @Id",
                new { dto.Score, dto.Feedback, Id = submissionId });

            return Ok(new { Message = "Submission graded successfully" });
        }

        /// <summary>
        /// A Student submits to an assignment their class offers. A second call
        /// replaces the link rather than adding a row, so the unique index holds.
        /// </summary>
        [HttpPost("submit")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> SubmitAssignment([FromBody] SubmitAssignmentDto dto)
        {
            using var db = Connection;
            var filePath = (dto.FilePath ?? string.Empty).Trim();
            if (filePath.Length == 0) return BadRequest(new { Message = "A file link or path is required." });
            if (filePath.Length > 500) return BadRequest(new { Message = "The file link must be 500 characters or fewer." });

            var studentId = await StudentIdForAsync(db, CurrentUserId());
            if (!studentId.HasValue) return BadRequest(new { Message = "No student record for this account." });

            var assignment = await db.QuerySingleOrDefaultAsync<AssignmentRow>(
                "SELECT Id, SubjectId, TeacherId, MaxScore FROM Assignments WHERE Id = @Id",
                new { Id = dto.AssignmentId });
            if (assignment == null) return NotFound(new { Message = "Assignment not found." });

            var classId = await ClassIdForStudentAsync(db, studentId.Value);
            if (!classId.HasValue) return BadRequest(new { Message = "You are not enrolled in a class yet." });

            var offered = await db.ExecuteScalarAsync<bool>(
                "SELECT EXISTS (SELECT 1 FROM ClassSubjects WHERE ClassId = @c AND SubjectId = @s)",
                new { c = classId.Value, s = assignment.SubjectId });
            if (!offered) return BadRequest(new { Message = "That assignment is not set for your class." });

            var id = await db.ExecuteScalarAsync<int>(
                @"INSERT INTO AssignmentSubmissions (AssignmentId, StudentId, FilePath, SubmittedAt)
                  VALUES (@AssignmentId, @StudentId, @FilePath, CURRENT_TIMESTAMP)
                  ON CONFLICT (AssignmentId, StudentId)
                  DO UPDATE SET FilePath = EXCLUDED.FilePath, SubmittedAt = CURRENT_TIMESTAMP
                  RETURNING Id",
                new { dto.AssignmentId, StudentId = studentId.Value, FilePath = filePath });

            return Ok(new { Message = "Assignment submitted successfully", SubmissionId = id });
        }

        private static string? ValidateAssignment(
            SaveAssignmentDto dto, out string title, out string? description, out string? attachment)
        {
            title = (dto.Title ?? string.Empty).Trim();
            description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            attachment = string.IsNullOrWhiteSpace(dto.AttachmentUrl) ? null : dto.AttachmentUrl.Trim();

            if (title.Length == 0) return "Assignment title is required.";
            if (title.Length > 255) return "Assignment title must be 255 characters or fewer.";
            if (dto.SubjectId <= 0) return "A subject is required.";
            if (dto.DueDate == default) return "A due date is required.";
            if (dto.MaxScore <= 0) return "Max score must be greater than zero.";
            if (dto.MaxScore > 999.99m) return "Max score must be 999.99 or less.";
            if (attachment is { Length: > 500 }) return "The attachment link must be 500 characters or fewer.";
            return null;
        }

        /// <summary>Admin may manage any assignment; a teacher only their own.</summary>
        private static async Task<bool> CanManageAsync(
            IDbConnection db, AssignmentRow assignment, int userId, bool isAdmin)
        {
            if (isAdmin) return true;
            var teacherId = await TeacherIdForAsync(db, userId);
            return teacherId.HasValue
                && assignment.TeacherId.HasValue
                && assignment.TeacherId.Value == teacherId.Value;
        }
    }

    public class SaveAssignmentDto
    {
        public int SubjectId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime DueDate { get; set; }
        public decimal MaxScore { get; set; }
        public string? AttachmentUrl { get; set; }
    }

    public class SubmitAssignmentDto
    {
        public int AssignmentId { get; set; }
        public string FilePath { get; set; } = string.Empty;
    }

    public class GradeSubmissionDto
    {
        public decimal? Score { get; set; }
        public string? Feedback { get; set; }
    }
}
