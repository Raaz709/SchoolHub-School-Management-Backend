using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;

namespace SchoolHub.API.Controllers
{
    [Route("api/reports")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class ReportsController : ControllerBase
    {
        private readonly string _connectionString;

        public ReportsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        [HttpGet("students-by-class")]
        public async Task<IActionResult> GetStudentsByClassReport()
        {
            using var db = Connection;
            var sql = @"
                SELECT c.Name as ClassName, sec.Name as SectionName, COUNT(e.StudentId) as StudentCount
                FROM Classes c
                LEFT JOIN Sections sec ON c.Id = sec.ClassId
                LEFT JOIN Enrollments e ON c.Id = e.ClassId AND (e.SectionId = sec.Id OR e.SectionId IS NULL)
                GROUP BY c.Name, sec.Name";
            return Ok(await db.QueryAsync(sql));
        }

        [HttpGet("fee-collection")]
        public async Task<IActionResult> GetFeeCollectionReport()
        {
            using var db = Connection;
            var sql = @"
                SELECT sf.Status, COUNT(sf.Id) as Count, SUM(fs.Amount) as TotalAmount
                FROM StudentFees sf
                JOIN FeeStructures fs ON sf.FeeStructureId = fs.Id
                GROUP BY sf.Status";
            return Ok(await db.QueryAsync(sql));
        }
    }
}
