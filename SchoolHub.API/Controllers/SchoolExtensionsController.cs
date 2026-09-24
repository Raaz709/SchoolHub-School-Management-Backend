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
    public class SchoolExtensionsController : ControllerBase
    {
        private readonly string _connectionString;

        public SchoolExtensionsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        // --- ACADEMIC YEARS ---
        [HttpGet("academic-years")]
        public async Task<IActionResult> GetAcademicYears()
        {
            using var db = Connection;
            return Ok(await db.QueryAsync("SELECT * FROM AcademicYears"));
        }

        [HttpPost("academic-years")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateAcademicYear([FromBody] CreateAcademicYearDto dto)
        {
            using var db = Connection;
            var sql = "INSERT INTO AcademicYears (Name, StartDate, EndDate, IsCurrent) VALUES (@Name, @StartDate, @EndDate, @IsCurrent) RETURNING Id;";
            var id = await db.ExecuteScalarAsync<int>(sql, dto);
            return Ok(new { Message = "Academic year created successfully", Id = id });
        }

        // --- TIMETABLE ---
        [HttpGet("timetable")]
        public async Task<IActionResult> GetTimetable([FromQuery] int classId, [FromQuery] int sectionId)
        {
            using var db = Connection;
            var sql = @"
                SELECT t.Id, t.DayOfWeek, ts.StartTime, ts.EndTime, sub.Name as SubjectName, u.Username as TeacherName
                FROM TimetableEntries t
                JOIN TimeSlots ts ON t.TimeSlotId = ts.Id
                JOIN Subjects sub ON t.SubjectId = sub.Id
                LEFT JOIN Teachers th ON t.TeacherId = th.Id
                LEFT JOIN Users u ON th.UserId = u.Id
                WHERE t.ClassId = @ClassId AND t.SectionId = @SectionId";
            return Ok(await db.QueryAsync(sql, new { ClassId = classId, SectionId = sectionId }));
        }

        [HttpPost("timetable")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateTimetableEntry([FromBody] CreateTimetableDto dto)
        {
            using var db = Connection;
            var sql = @"
                INSERT INTO TimetableEntries (ClassId, SectionId, SubjectId, TeacherId, TimeSlotId, DayOfWeek) 
                VALUES (@ClassId, @SectionId, @SubjectId, @TeacherId, @TimeSlotId, @DayOfWeek) 
                RETURNING Id;";
            var id = await db.ExecuteScalarAsync<int>(sql, dto);
            return Ok(new { Message = "Timetable entry created successfully", Id = id });
        }

        // --- EVENTS ---
        [HttpGet("events")]
        public async Task<IActionResult> GetEvents()
        {
            using var db = Connection;
            return Ok(await db.QueryAsync("SELECT * FROM Events ORDER BY EventDate ASC"));
        }

        [HttpPost("events")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateEvent([FromBody] CreateEventDto dto)
        {
            using var db = Connection;
            var sql = "INSERT INTO Events (Title, Description, EventDate, Location) VALUES (@Title, @Description, @EventDate, @Location) RETURNING Id;";
            var id = await db.ExecuteScalarAsync<int>(sql, dto);
            return Ok(new { Message = "Event created successfully", Id = id });
        }

        // --- NOTIFICATIONS ---
        [HttpGet("notifications")]
        public async Task<IActionResult> GetNotifications()
        {
            using var db = Connection;
            return Ok(await db.QueryAsync("SELECT * FROM Notifications ORDER BY CreatedAt DESC"));
        }

        [HttpPost("notifications")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> CreateNotification([FromBody] CreateNotificationDto dto)
        {
            using var db = Connection;
            var sql = "INSERT INTO Notifications (Title, Message) VALUES (@Title, @Message) RETURNING Id;";
            var id = await db.ExecuteScalarAsync<int>(sql, dto);
            return Ok(new { Message = "Notification sent successfully", Id = id });
        }
    }

    public class CreateAcademicYearDto
    {
        public string Name { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsCurrent { get; set; }
    }

    public class CreateTimetableDto
    {
        public int ClassId { get; set; }
        public int SectionId { get; set; }
        public int SubjectId { get; set; }
        public int TeacherId { get; set; }
        public int TimeSlotId { get; set; }
        public int DayOfWeek { get; set; }
    }

    public class CreateEventDto
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public string Location { get; set; } = string.Empty;
    }

    public class CreateNotificationDto
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
