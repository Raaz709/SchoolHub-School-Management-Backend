using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;

namespace SchoolHub.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AuditLogsController : ControllerBase
    {
        private readonly string _connectionString;

        public AuditLogsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        [HttpGet]
        public async Task<IActionResult> GetAuditLogs()
        {
            using var db = Connection;
            var logs = await db.QueryAsync(@"
                SELECT a.Id, a.Action, a.Details, a.IpAddress, a.CreatedAt, u.Username 
                FROM AuditLogs a
                LEFT JOIN Users u ON a.UserId = u.Id
                ORDER BY a.CreatedAt DESC");
            return Ok(logs);
        }
    }
}
