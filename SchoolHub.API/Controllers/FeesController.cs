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
    public class FeesController : ControllerBase
    {
        private readonly string _connectionString;

        public FeesController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        [HttpGet("structures")]
        public async Task<IActionResult> GetFeeStructures()
        {
            using var db = Connection;
            var structures = await db.QueryAsync("SELECT * FROM FeeStructures");
            return Ok(structures);
        }

        [HttpPost("structures")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateFeeStructure([FromBody] CreateFeeStructureDto dto)
        {
            using var db = Connection;
            var sql = "INSERT INTO FeeStructures (Name, Amount, ClassId) VALUES (@Name, @Amount, @ClassId) RETURNING Id;";
            var id = await db.ExecuteScalarAsync<int>(sql, dto);
            return Ok(new { Message = "Fee structure created successfully", FeeStructureId = id });
        }

        [HttpPost("payments")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RecordPayment([FromBody] RecordPaymentDto dto)
        {
            using var db = Connection;
            var sql = @"
                INSERT INTO Payments (InvoiceId, AmountPaid, PaymentMethod, TransactionReference) 
                VALUES (@InvoiceId, @AmountPaid, @PaymentMethod, @TransactionReference) 
                RETURNING Id;";
            var id = await db.ExecuteScalarAsync<int>(sql, dto);
            return Ok(new { Message = "Payment recorded successfully", PaymentId = id });
        }
    }

    public class CreateFeeStructureDto
    {
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public int ClassId { get; set; }
    }

    public class RecordPaymentDto
    {
        public int InvoiceId { get; set; }
        public decimal AmountPaid { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
        public string TransactionReference { get; set; } = string.Empty;
    }
}
