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
    public class AcademicController : ControllerBase
    {
        private readonly string _connectionString;

        public AcademicController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        [HttpGet("classes")]
        public async Task<IActionResult> GetClasses()
        {
            using var db = Connection;
            var classes = await db.QueryAsync("SELECT * FROM Classes");
            return Ok(classes);
        }

        [HttpPost("classes")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateClass([FromBody] CreateClassDto dto)
        {
            using var db = Connection;
            var sql = "INSERT INTO Classes (Name) VALUES (@Name) RETURNING Id;";
            var id = await db.ExecuteScalarAsync<int>(sql, new { dto.Name });
            return Ok(new { Message = "Class created successfully", ClassId = id });
        }

        [HttpGet("sections")]
        public async Task<IActionResult> GetSections()
        {
            using var db = Connection;
            var sections = await db.QueryAsync(@"
                SELECT s.Id, s.Name, c.Name as ClassName 
                FROM Sections s
                JOIN Classes c ON s.ClassId = c.Id");
            return Ok(sections);
        }

        [HttpPost("sections")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateSection([FromBody] CreateSectionDto dto)
        {
            using var db = Connection;
            var sql = "INSERT INTO Sections (Name, ClassId) VALUES (@Name, @ClassId) RETURNING Id;";
            var id = await db.ExecuteScalarAsync<int>(sql, new { dto.Name, dto.ClassId });
            return Ok(new { Message = "Section created successfully", SectionId = id });
        }

        [HttpGet("subjects")]
        public async Task<IActionResult> GetSubjects()
        {
            using var db = Connection;
            var subjects = await db.QueryAsync("SELECT * FROM Subjects");
            return Ok(subjects);
        }

        [HttpPost("subjects")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateSubject([FromBody] CreateSubjectDto dto)
        {
            using var db = Connection;
            var sql = "INSERT INTO Subjects (Name, Code) VALUES (@Name, @Code) RETURNING Id;";
            var id = await db.ExecuteScalarAsync<int>(sql, new { dto.Name, dto.Code });
            return Ok(new { Message = "Subject created successfully", SubjectId = id });
        }
    }

    public class CreateClassDto
    {
        public string Name { get; set; } = string.Empty;
    }

    public class CreateSectionDto
    {
        public string Name { get; set; } = string.Empty;
        public int ClassId { get; set; }
    }

    public class CreateSubjectDto
    {
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }
}
