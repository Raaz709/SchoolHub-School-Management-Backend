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
    public class AttendanceController : ControllerBase
    {
        private readonly string _connectionString;

        public AttendanceController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        [HttpPost("session")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> CreateAttendanceSession([FromBody] CreateAttendanceSessionDto dto)
        {
            using var db = Connection;
            db.Open();
            using var transaction = db.BeginTransaction();

            try
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                int.TryParse(userIdStr, out int userId);

                // Get Teacher Id if user is teacher
                int? teacherId = await db.ExecuteScalarAsync<int?>(
                    "SELECT Id FROM Teachers WHERE UserId = @UserId", new { UserId = userId }, transaction);

                var sessionSql = @"
                    INSERT INTO AttendanceSessions (ClassId, SectionId, TeacherId, Date) 
                    VALUES (@ClassId, @SectionId, @TeacherId, @Date) 
                    RETURNING Id;";
                
                var sessionId = await db.ExecuteScalarAsync<int>(sessionSql, new { 
                    dto.ClassId, 
                    dto.SectionId, 
                    TeacherId = teacherId, 
                    Date = dto.Date 
                }, transaction);

                foreach (var rec in dto.Records)
                {
                    var recordSql = @"
                        INSERT INTO AttendanceRecords (SessionId, StudentId, Status, Remarks) 
                        VALUES (@SessionId, @StudentId, @Status, @Remarks);";
                    await db.ExecuteAsync(recordSql, new { 
                        SessionId = sessionId, 
                        rec.StudentId, 
                        rec.Status, 
                        rec.Remarks 
                    }, transaction);
                }

                // Audit log
                await db.ExecuteAsync(@"
                    INSERT INTO AuditLogs (UserId, Action, Details) 
                    VALUES (@UserId, 'MARK_ATTENDANCE', @Details)", 
                    new { UserId = userId, Details = $"Marked attendance for class {dto.ClassId} on {dto.Date:yyyy-MM-dd}" }, transaction);

                transaction.Commit();
                return Ok(new { Message = "Attendance marked successfully", SessionId = sessionId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        [HttpGet("student/{studentId}")]
        public async Task<IActionResult> GetStudentAttendance(int studentId)
        {
            using var db = Connection;
            var sql = @"
                ar.Id, ar.Status, ar.Remarks, s.Date, s.ClassId
                FROM AttendanceRecords ar
                JOIN AttendanceSessions s ON ar.SessionId = s.Id
                WHERE ar.StudentId = @StudentId";
            var records = await db.QueryAsync(sql, new { StudentId = studentId });
            return Ok(records);
        }
    }

    public class CreateAttendanceSessionDto
    {
        public int ClassId { get; set; }
        public int SectionId { get; set; }
        public DateTime Date { get; set; }
        public List<AttendanceRecordItemDto> Records { get; set; } = new();
    }

    public class AttendanceRecordItemDto
    {
        public int StudentId { get; set; }
        public string Status { get; set; } = "Present"; // Present, Absent, Late, Excused
        public string Remarks { get; set; } = string.Empty;
    }
}
