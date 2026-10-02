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

        /// <summary>
        /// How much is billed, collected and outstanding, grouped by derived
        /// status. The status is computed here with the same precedence as
        /// <see cref="FeesController.Derive"/>: paid beats overdue beats partial
        /// beats unpaid. <c>StudentFees</c> no longer has a Status column for the
        /// report to group on, so it must not read one.
        /// </summary>
        [HttpGet("fee-collection")]
        public async Task<IActionResult> GetFeeCollectionReport()
        {
            using var db = Connection;
            var rows = await db.QueryAsync<FeeCollectionReportRow>(@"
                SELECT CASE
                           WHEN COALESCE(p.Paid, 0) >= fs.Amount THEN 'Paid'
                           WHEN sf.DueDate < CURRENT_DATE THEN 'Overdue'
                           WHEN COALESCE(p.Paid, 0) > 0 THEN 'Partial'
                           ELSE 'Unpaid'
                       END AS Status,
                       count(*) AS FeeCount,
                       COALESCE(sum(fs.Amount), 0) AS TotalAmount,
                       COALESCE(sum(COALESCE(p.Paid, 0)), 0) AS TotalPaid,
                       COALESCE(sum(fs.Amount - COALESCE(p.Paid, 0)), 0) AS TotalOutstanding
                FROM StudentFees sf
                JOIN FeeStructures fs ON fs.Id = sf.FeeStructureId
                LEFT JOIN LATERAL (
                    SELECT sum(pp.AmountPaid) AS Paid
                      FROM Payments pp WHERE pp.StudentFeeId = sf.Id
                ) p ON TRUE
                GROUP BY 1
                ORDER BY 1");
            return Ok(rows);
        }

        public class FeeCollectionReportRow
        {
            public string Status { get; set; } = string.Empty;
            public int FeeCount { get; set; }
            public decimal TotalAmount { get; set; }
            public decimal TotalPaid { get; set; }
            public decimal TotalOutstanding { get; set; }
        }
    }
}
