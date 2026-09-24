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
    public class ExamsController : ControllerBase
    {
        private readonly string _connectionString;

        public ExamsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        [HttpGet]
        public async Task<IActionResult> GetExams()
        {
            using var db = Connection;
            var exams = await db.QueryAsync("SELECT * FROM Exams");
            return Ok(exams);
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> CreateExam([FromBody] CreateExamDto dto)
        {
            using var db = Connection;
            var sql = "INSERT INTO Exams (Title, AcademicYearId, StartDate, EndDate) VALUES (@Title, @AcademicYearId, @StartDate, @EndDate) RETURNING Id;";
            var id = await db.ExecuteScalarAsync<int>(sql, dto);
            return Ok(new { Message = "Exam created successfully", ExamId = id });
        }

        [HttpPost("marks")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> EnterMarks([FromBody] EnterMarksDto dto)
        {
            using var db = Connection;
            db.Open();
            using var transaction = db.BeginTransaction();

            try
            {
                // Get max marks for exam subject
                decimal maxMarks = await db.ExecuteScalarAsync<decimal>(
                    "SELECT MaxMarks FROM ExamSubjects WHERE Id = @ExamSubjectId", 
                    new { dto.ExamSubjectId }, transaction);

                decimal percentage = (dto.MarksObtained / maxMarks) * 100;
                string grade = percentage >= 90 ? "A+" : percentage >= 80 ? "A" : percentage >= 70 ? "B" : percentage >= 60 ? "C" : "F";

                var existingId = await db.ExecuteScalarAsync<int?>(
                    "SELECT Id FROM Marks WHERE ExamSubjectId = @ExamSubjectId AND StudentId = @StudentId",
                    new { dto.ExamSubjectId, dto.StudentId }, transaction);

                if (existingId.HasValue)
                {
                    var updateSql = "UPDATE Marks SET MarksObtained = @MarksObtained, Grade = @Grade, Remarks = @Remarks WHERE Id = @Id";
                    await db.ExecuteAsync(updateSql, new { dto.MarksObtained, Grade = grade, dto.Remarks, Id = existingId.Value }, transaction);
                }
                else
                {
                    var insertSql = @"
                        INSERT INTO Marks (ExamSubjectId, StudentId, MarksObtained, Grade, Remarks) 
                        VALUES (@ExamSubjectId, @StudentId, @MarksObtained, @Grade, @Remarks)";
                    await db.ExecuteAsync(insertSql, new { dto.ExamSubjectId, dto.StudentId, dto.MarksObtained, Grade = grade, dto.Remarks }, transaction);
                }

                transaction.Commit();
                return Ok(new { Message = "Marks entered and graded successfully", Grade = grade, Percentage = percentage });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { Error = ex.Message });
            }
        }
    }

    public class CreateExamDto
    {
        public string Title { get; set; } = string.Empty;
        public int AcademicYearId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class EnterMarksDto
    {
        public int ExamSubjectId { get; set; }
        public int StudentId { get; set; }
        public decimal MarksObtained { get; set; }
        public string Remarks { get; set; } = string.Empty;
    }
}
