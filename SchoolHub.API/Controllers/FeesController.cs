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
        /// <summary>Fee collection is an Admin screen. Learners see their own ledger
        /// via the scoped /api/students/{id}/fees route instead.</summary>
        private static readonly string[] PaymentMethods =
            { "Cash", "Card", "Bank Transfer", "Cheque", "Mobile Money" };

        private readonly string _connectionString;

        public FeesController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        // ============================================================
        // FEE STRUCTURES
        // ============================================================

        /// <summary>
        /// Was SELECT *, so the response changed shape whenever the table did and
        /// carried no class name or assignment count for the screen to show.
        /// </summary>
        [HttpGet("structures")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetFeeStructures()
        {
            using var db = Connection;
            var structures = await db.QueryAsync<FeeStructureRow>(@"
                SELECT fs.Id,
                       fs.Name,
                       fs.Amount,
                       fs.ClassId,
                       c.Name AS ClassName,
                       (SELECT count(*) FROM StudentFees sf WHERE sf.FeeStructureId = fs.Id) AS AssignedCount
                FROM FeeStructures fs
                LEFT JOIN Classes c ON c.Id = fs.ClassId
                ORDER BY fs.Name");
            return Ok(structures);
        }

        [HttpPost("structures")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateFeeStructure([FromBody] SaveFeeStructureDto dto)
        {
            var invalid = ValidateStructure(dto);
            if (invalid != null) return invalid;

            using var db = Connection;
            if (await StructureNameTakenAsync(db, dto.Name, null))
                return Conflict(new { Message = "A fee structure with that name already exists." });

            if (dto.ClassId.HasValue && !await ClassExistsAsync(db, dto.ClassId.Value))
                return BadRequest(new { Message = "That class does not exist." });

            var id = await db.ExecuteScalarAsync<int>(@"
                INSERT INTO FeeStructures (Name, Amount, ClassId)
                VALUES (btrim(@Name), @Amount, @ClassId)
                RETURNING Id", dto);

            return Ok(new { Message = "Fee structure created successfully", FeeStructureId = id });
        }

        /// <summary>
        /// Amount is guarded once assigned: changing it rewrites what every
        /// assigned student already owes, including students part-way through
        /// paying the old figure.
        /// </summary>
        [HttpPut("structures/{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateFeeStructure(int id, [FromBody] SaveFeeStructureDto dto)
        {
            var invalid = ValidateStructure(dto);
            if (invalid != null) return invalid;

            using var db = Connection;
            var existing = await db.QuerySingleOrDefaultAsync<(decimal Amount, string Name)?>(@"
                SELECT Amount, Name FROM FeeStructures WHERE Id = @Id", new { Id = id });
            if (existing == null) return NotFound(new { Message = "That fee structure does not exist." });

            if (await StructureNameTakenAsync(db, dto.Name, id))
                return Conflict(new { Message = "A fee structure with that name already exists." });

            if (dto.ClassId.HasValue && !await ClassExistsAsync(db, dto.ClassId.Value))
                return BadRequest(new { Message = "That class does not exist." });

            if (existing.Value.Amount != dto.Amount)
            {
                var assigned = await db.ExecuteScalarAsync<int>(
                    "SELECT count(*) FROM StudentFees WHERE FeeStructureId = @Id", new { Id = id });
                if (assigned > 0)
                {
                    return Conflict(new
                    {
                        Message = $"{assigned} student(s) already have this fee assigned. Remove those assignments before changing the amount, so what each student owes does not change under them."
                    });
                }
            }

            await db.ExecuteAsync(@"
                UPDATE FeeStructures
                   SET Name = btrim(@Name), Amount = @Amount, ClassId = @ClassId
                 WHERE Id = @Id", new { dto.Name, dto.Amount, dto.ClassId, Id = id });

            return Ok(new { Message = "Fee structure updated successfully", FeeStructureId = id });
        }

        /// <summary>
        /// Removing a structure cascades away its assignments and the payments
        /// under them, so it is refused while any exist — the same guard
        /// AcademicController applies to a class still holding an exam paper.
        /// </summary>
        [HttpDelete("structures/{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteFeeStructure(int id)
        {
            using var db = Connection;
            if (await db.QuerySingleOrDefaultAsync<int?>(
                    "SELECT Id FROM FeeStructures WHERE Id = @Id", new { Id = id }) == null)
            {
                return NotFound(new { Message = "That fee structure does not exist." });
            }

            var (assigned, collected) = await db.QuerySingleAsync<(int Assigned, decimal Collected)>(@"
                SELECT (SELECT count(*) FROM StudentFees WHERE FeeStructureId = @Id) AS Assigned,
                       (SELECT COALESCE(sum(p.AmountPaid), 0)
                          FROM Payments p
                          JOIN StudentFees sf ON sf.Id = p.StudentFeeId
                         WHERE sf.FeeStructureId = @Id) AS Collected", new { Id = id });

            if (assigned > 0)
            {
                return Conflict(new
                {
                    Message = assigned == 1
                        ? "1 student has this fee assigned"
                          + (collected > 0 ? $" and {collected:N2} has been collected against it." : ".")
                          + " Remove the assignment first."
                        : $"{assigned} students have this fee assigned"
                          + (collected > 0 ? $" and {collected:N2} has been collected against them." : ".")
                          + " Remove the assignments first."
                });
            }

            await db.ExecuteAsync("DELETE FROM FeeStructures WHERE Id = @Id", new { Id = id });
            return Ok(new { Message = "Fee structure deleted successfully" });
        }

        // ============================================================
        // ASSIGNMENTS  (StudentFees is the receivable ledger)
        // ============================================================

        /// <summary>
        /// One row per fee a student owes. Paid, outstanding and status are
        /// computed here rather than read from a column, so they cannot disagree
        /// with the payments behind them.
        /// </summary>
        [HttpGet("assignments")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAssignments([FromQuery] int? classId, [FromQuery] string? status, [FromQuery] string? search)
        {
            using var db = Connection;
            var rows = (await db.QueryAsync<AssignmentRow>(@"
                SELECT sf.Id,
                       sf.StudentId,
                       u.Username AS StudentName,
                       st.RollNumber AS AdmissionNumber,
                       e.ClassId,
                       e.ClassName,
                       sf.FeeStructureId,
                       fs.Name AS FeeName,
                       fs.Amount,
                       sf.DueDate,
                       COALESCE(paid.Paid, 0) AS Paid,
                       fs.Amount - COALESCE(paid.Paid, 0) AS Outstanding
                FROM StudentFees sf
                JOIN FeeStructures fs ON fs.Id = sf.FeeStructureId
                JOIN Students st ON st.Id = sf.StudentId
                  JOIN Users u ON u.Id = st.UserId
                -- A student's class comes from their first enrolment. Joining
                -- Enrollments directly would repeat the whole ledger once per
                -- class they have ever been in.
                LEFT JOIN LATERAL (
                    SELECT en.ClassId, c.Name AS ClassName
                      FROM Enrollments en
                      LEFT JOIN Classes c ON c.Id = en.ClassId
                     WHERE en.StudentId = st.Id
                     ORDER BY en.Id
                     LIMIT 1
                ) e ON TRUE
                LEFT JOIN LATERAL (
                    SELECT sum(p.AmountPaid) AS Paid
                      FROM Payments p
                     WHERE p.StudentFeeId = sf.Id
                ) paid ON TRUE
                WHERE (@ClassId IS NULL OR e.ClassId = @ClassId)
                  AND (@Search IS NULL
                       OR u.Username ILIKE '%' || @Search || '%'
                       OR st.RollNumber ILIKE '%' || @Search || '%')
                ORDER BY sf.DueDate, u.Username",
                new
                {
                    ClassId = classId,
                    Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim()
                })).Select(r => new
                {
                    r.Id,
                    r.StudentId,
                    r.StudentName,
                    r.AdmissionNumber,
                    r.ClassId,
                    r.ClassName,
                    r.FeeStructureId,
                    r.FeeName,
                    r.Amount,
                    r.DueDate,
                    r.Paid,
                    r.Outstanding,
                    Status = Derive(r.Amount, r.Paid, r.DueDate)
                })
                // Filtered here rather than in SQL so the status filter and the
                // status column can never be derived two different ways.
                .Where(r => string.IsNullOrWhiteSpace(status)
                            || r.Status.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));

            return Ok(rows);
        }

        /// <summary>
        /// Assigns to one student or to every student in a class. The unique index
        /// on (StudentId, FeeStructureId) makes a repeat a no-op instead of a
        /// second charge, so this is safe to re-run after a partial failure.
        /// </summary>
        [HttpPost("assignments")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AssignFee([FromBody] AssignFeeDto dto)
        {
            if (dto.FeeStructureId <= 0)
                return BadRequest(new { Message = "Choose a fee structure." });
            if (dto.StudentId == null && dto.ClassId == null)
                return BadRequest(new { Message = "Choose a student or a class to assign this fee to." });
            if (dto.StudentId != null && dto.ClassId != null)
                return BadRequest(new { Message = "Assign this fee to either one student or one class, not both." });

            using var db = Connection;
            var structure = await db.QuerySingleOrDefaultAsync<(int Id, string Name, int? ClassId)?>(@"
                SELECT fs.Id, fs.Name, fs.ClassId
                  FROM FeeStructures fs WHERE fs.Id = @Id", new { Id = dto.FeeStructureId });
            if (structure == null) return NotFound(new { Message = "That fee structure does not exist." });

            // The structure is either school-wide (ClassId null) or belongs to one
            // class. Charging it to a different grade would be a mis-pick.
            if (structure.Value.ClassId.HasValue && structure.Value.ClassId != dto.ClassId)
            {
                return BadRequest(new
                {
                    Message = $"'{structure.Value.Name}' belongs to another class. Assign it to that class, or to a single student."
                });
            }

            IReadOnlyList<int> studentIds;
            if (dto.StudentId is int sid)
            {
                if (await db.QuerySingleOrDefaultAsync<int?>(
                        "SELECT Id FROM Students WHERE Id = @Id", new { Id = sid }) == null)
                {
                    return BadRequest(new { Message = "That student does not exist." });
                }
                studentIds = new[] { sid };
            }
            else
            {
                if (!await ClassExistsAsync(db, dto.ClassId!.Value))
                    return BadRequest(new { Message = "That class does not exist." });

                studentIds = (await db.QueryAsync<int>(@"
                    SELECT DISTINCT en.StudentId
                      FROM Enrollments en
                     WHERE en.ClassId = @ClassId", new { ClassId = dto.ClassId })).ToList();
                if (studentIds.Count == 0)
                    return BadRequest(new { Message = "That class has no students enrolled." });
            }

            var inserted = await db.ExecuteAsync(@"
                INSERT INTO StudentFees (StudentId, FeeStructureId, DueDate)
                SELECT unnest(@StudentIds), @FeeStructureId, @DueDate
                ON CONFLICT (StudentId, FeeStructureId) DO NOTHING",
                new { StudentIds = studentIds.ToArray(), dto.FeeStructureId, dto.DueDate });

            var scope = dto.StudentId != null ? "this student" : $"{studentIds.Count} student(s)";
            return Ok(new
            {
                Message = inserted == 0
                    ? $"{scope} already had this fee assigned."
                    : $"Assigned to {inserted} of {(dto.StudentId != null ? "1 student" : $"{studentIds.Count} students")}.",
                Assigned = inserted,
                Skipped = studentIds.Count - inserted
            });
        }

        /// <summary>
        /// Refused once money has been taken, because the cascade would take the
        /// payments with it.
        /// </summary>
        [HttpDelete("assignments/{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RemoveAssignment(int id)
        {
            using var db = Connection;
            if (await db.QuerySingleOrDefaultAsync<int?>(
                    "SELECT Id FROM StudentFees WHERE Id = @Id", new { Id = id }) == null)
            {
                return NotFound(new { Message = "That fee assignment does not exist." });
            }

            var collected = await db.ExecuteScalarAsync<decimal>(
                "SELECT COALESCE(sum(AmountPaid), 0) FROM Payments WHERE StudentFeeId = @Id", new { Id = id });
            if (collected > 0)
            {
                return Conflict(new
                {
                    Message = $"{collected:N2} has been collected against this fee. Remove the assignment once the payment is settled."
                });
            }

            await db.ExecuteAsync("DELETE FROM StudentFees WHERE Id = @Id", new { Id = id });
            return Ok(new { Message = "Fee assignment removed" });
        }

        // ============================================================
        // PAYMENTS
        // ============================================================

        [HttpGet("payments")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetPayments([FromQuery] int? studentId)
        {
            using var db = Connection;
            var payments = await db.QueryAsync<PaymentRow>(@"
                SELECT p.Id,
                       p.StudentFeeId,
                       sf.StudentId,
                       u.Username AS StudentName,
                       st.RollNumber AS AdmissionNumber,
                       fs.Name AS FeeName,
                       fs.Amount,
                       p.AmountPaid,
                       p.PaymentDate,
                       p.PaymentMethod,
                       p.TransactionReference
                FROM Payments p
                JOIN StudentFees sf ON sf.Id = p.StudentFeeId
                JOIN Students st ON st.Id = sf.StudentId
                  JOIN Users u ON u.Id = st.UserId
                JOIN FeeStructures fs ON fs.Id = sf.FeeStructureId
                WHERE (@StudentId IS NULL OR sf.StudentId = @StudentId)
                ORDER BY p.PaymentDate DESC, p.Id DESC", new { StudentId = studentId });

            return Ok(payments);
        }

        /// <summary>
        /// Settles one ledger row. Overpayment is refused rather than clamped, so a
        /// mistyped amount comes back to the collector instead of silently
        /// becoming untraceable credit.
        ///
        /// The ledger row is locked for the read-check-write so two collectors
        /// taking money on the same fee at once cannot both pass the balance
        /// check and jointly overpay it.
        /// </summary>
        [HttpPost("payments")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RecordPayment([FromBody] RecordPaymentDto dto)
        {
            if (dto.AmountPaid <= 0)
                return BadRequest(new { Message = "Enter an amount greater than zero." });

            var method = (dto.PaymentMethod ?? "Cash").Trim();
            if (!PaymentMethods.Contains(method, StringComparer.OrdinalIgnoreCase))
                return BadRequest(new { Message = $"Payment method must be one of: {string.Join(", ", PaymentMethods)}." });

            var reference = (dto.TransactionReference ?? "").Trim();
            if (reference.Length > 255)
                return BadRequest(new { Message = "Transaction reference is too long." });

            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();

            if (await db.ExecuteScalarAsync<int?>(
                    "SELECT Id FROM StudentFees WHERE Id = @Id FOR UPDATE", new { Id = dto.StudentFeeId })
                    == null)
            {
                tx.Rollback();
                return NotFound(new { Message = "That fee assignment does not exist." });
            }

            var fee = await db.QuerySingleAsync<(decimal Amount, decimal Paid, string FeeName, string StudentName)>(@"
                SELECT fs.Amount,
                       COALESCE((SELECT sum(p.AmountPaid) FROM Payments p WHERE p.StudentFeeId = sf.Id), 0) AS Paid,
                       fs.Name AS FeeName,
                       u.Username AS StudentName
                  FROM StudentFees sf
                  JOIN FeeStructures fs ON fs.Id = sf.FeeStructureId
                  JOIN Students st ON st.Id = sf.StudentId
                  JOIN Users u ON u.Id = st.UserId
                 WHERE sf.Id = @Id", new { Id = dto.StudentFeeId });

            var outstanding = fee.Amount - fee.Paid;
            if (outstanding <= 0)
            {
                tx.Rollback();
                return Conflict(new
                {
                    Message = $"'{fee.FeeName}' for {fee.StudentName} is already fully paid."
                });
            }
            if (dto.AmountPaid > outstanding)
            {
                tx.Rollback();
                return BadRequest(new
                {
                    Message = $"{dto.AmountPaid:N2} is more than the {outstanding:N2} outstanding on '{fee.FeeName}' for {fee.StudentName}."
                });
            }

            var id = await db.ExecuteScalarAsync<int>(@"
                INSERT INTO Payments (StudentFeeId, AmountPaid, PaymentMethod, TransactionReference)
                VALUES (@StudentFeeId, @AmountPaid, @PaymentMethod, @TransactionReference)
                RETURNING Id",
                new
                {
                    dto.StudentFeeId,
                    dto.AmountPaid,
                    PaymentMethod = Canonical(method),
                    TransactionReference = reference
                });

            tx.Commit();

            return Ok(new
            {
                Message = "Payment recorded successfully",
                PaymentId = id,
                Paid = fee.Paid + dto.AmountPaid,
                Outstanding = outstanding - dto.AmountPaid
            });
        }

        // ============================================================
        // COLLECTION SUMMARY
        // ============================================================

        [HttpGet("summary")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetSummary()
        {
            using var db = Connection;
            return Ok(await db.QuerySingleAsync<CollectionSummary>(@"
                SELECT COALESCE(sum(fs.Amount), 0) AS TotalBilled,
                       COALESCE(sum(p.Paid), 0) AS TotalCollected,
                       COALESCE(sum(fs.Amount), 0) - COALESCE(sum(p.Paid), 0) AS TotalOutstanding,
                       COALESCE(sum(CASE WHEN sf.DueDate < CURRENT_DATE
                                          AND COALESCE(p.Paid, 0) < fs.Amount
                                         THEN fs.Amount - COALESCE(p.Paid, 0) ELSE 0 END), 0) AS TotalOverdue,
                       (SELECT count(*) FROM Students) AS StudentCount
                FROM StudentFees sf
                JOIN FeeStructures fs ON fs.Id = sf.FeeStructureId
                LEFT JOIN LATERAL (
                    SELECT sum(p.AmountPaid) AS Paid
                      FROM Payments p WHERE p.StudentFeeId = sf.Id
                ) p ON TRUE"));
        }

        // ============================================================
        // HELPERS
        // ============================================================

        /// <summary>
        /// Unpaid and Partial come from the payments; Overdue is an unsettled fee
        /// past its due date and outranks them, so one Status field stays
        /// unambiguous for the screen to filter and colour on.
        ///
        /// Public and shared: the learner-scoped ledger in StudentsController and
        /// the frontend both need it to agree with the admin list.
        /// </summary>
        public static string Derive(decimal amount, decimal paid, DateTime dueDate)
        {
            if (paid >= amount) return "Paid";
            if (dueDate.Date < DateTime.UtcNow.Date) return "Overdue";
            return paid > 0 ? "Partial" : "Unpaid";
        }

        private static string Canonical(string method)
            => char.ToUpperInvariant(method[0]) + method[1..].ToLowerInvariant();

        private static async Task<bool> StructureNameTakenAsync(IDbConnection db, string name, int? exceptId)
            => await db.QuerySingleOrDefaultAsync<int?>(@"
                SELECT Id FROM FeeStructures
                 WHERE lower(btrim(Name)) = lower(btrim(@Name))
                   AND (@ExceptId IS NULL OR Id <> @ExceptId)",
                new { Name = name, ExceptId = exceptId }) is not null;

        private static async Task<bool> ClassExistsAsync(IDbConnection db, int classId)
            => await db.QuerySingleOrDefaultAsync<int?>(
                "SELECT Id FROM Classes WHERE Id = @Id", new { Id = classId }) is not null;

        private static ActionResult? ValidateStructure(SaveFeeStructureDto dto)
        {
            var name = (dto.Name ?? "").Trim();
            if (name.Length == 0) return new BadRequestObjectResult(new { Message = "Give the fee a name." });
            if (name.Length > 100) return new BadRequestObjectResult(new { Message = "Fee name is too long." });
            if (dto.Amount <= 0) return new BadRequestObjectResult(new { Message = "Enter an amount greater than zero." });
            if (dto.Amount > 10_000_000m)
                return new BadRequestObjectResult(new { Message = "That amount is larger than this system records." });
            if (dto.ClassId is <= 0) return new BadRequestObjectResult(new { Message = "That class does not exist." });
            return null;
        }
    }

    // ============================================================
    // DTOs AND ROW SHAPES
    // ============================================================

    public class SaveFeeStructureDto
    {
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; }

        /// <summary>Null means the fee applies school-wide.</summary>
        public int? ClassId { get; set; }
    }

    public class AssignFeeDto
    {
        public int FeeStructureId { get; set; }

        /// <summary>Assign to one student.</summary>
        public int? StudentId { get; set; }

        /// <summary>Assign to every student enrolled in this class.</summary>
        public int? ClassId { get; set; }

        public DateTime DueDate { get; set; } = DateTime.UtcNow.Date;
    }

    public class RecordPaymentDto
    {
        public int StudentFeeId { get; set; }
        public decimal AmountPaid { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
        public string TransactionReference { get; set; } = string.Empty;
    }

    public class FeeStructureRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public int? ClassId { get; set; }
        public string? ClassName { get; set; }
        public int AssignedCount { get; set; }
    }

    public class AssignmentRow
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public string? AdmissionNumber { get; set; }
        public int? ClassId { get; set; }
        public string? ClassName { get; set; }
        public int FeeStructureId { get; set; }
        public string FeeName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime DueDate { get; set; }
        public decimal Paid { get; set; }
        public decimal Outstanding { get; set; }
    }

    public class PaymentRow
    {
        public int Id { get; set; }
        public int StudentFeeId { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public string? AdmissionNumber { get; set; }
        public string FeeName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal AmountPaid { get; set; }
        public DateTimeOffset PaymentDate { get; set; }
        public string? PaymentMethod { get; set; }
        public string? TransactionReference { get; set; }
    }

    public class CollectionSummary
    {
        public decimal TotalBilled { get; set; }
        public decimal TotalCollected { get; set; }
        public decimal TotalOutstanding { get; set; }
        public decimal TotalOverdue { get; set; }
        public int StudentCount { get; set; }
    }
}
