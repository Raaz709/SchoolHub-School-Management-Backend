using Dapper;
using Npgsql;
using SchoolHub.API.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace SchoolHub.Tests;

/// <summary>
/// Covers the fee collection flow.
///
/// Fees had no write path before this. StudentFees was empty in every database
/// because nothing could insert a row, and the only money endpoint required an
/// invoice that nothing created either. What is worth protecting now that the
/// flow is reachable:
///
///   1. Status is derived from payments, never stored. The old column said
///      'Unpaid' on a fee that had been paid in full because no code path
///      recomputed it.
///   2. A fee cannot be overpaid. The amount is refused, not clamped, so a
///      mistyped figure comes back to the collector.
///   3. Assigning the same fee to the same student twice is a no-op, not a
///      second charge.
///   4. Money that has been taken blocks removing the assignment or the
///      structure, because the cascade would take the payment with it.
/// </summary>
[Collection(AuthTestCollection.Name)]
public class FeesTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Database=schoolhub_db;Username=postgres;Password=00000";

    private readonly AuthTestFixture _fixture;
    private readonly HttpClient _admin;
    private readonly HttpClient _teacher;
    private readonly HttpClient _student;

    public FeesTests(AuthTestFixture fixture)
    {
        _fixture = fixture;
        _admin = AuthTestFixture.ClientFor(fixture, fixture.AdminToken);
        _teacher = AuthTestFixture.ClientFor(fixture, fixture.TeacherToken);
        _student = AuthTestFixture.ClientFor(fixture, fixture.StudentToken);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _admin.Dispose();
        _teacher.Dispose();
        _student.Dispose();
        await ResetAsync();
    }

    /// <summary>
    /// Removes every FE- throwaway row. Deleting the structure cascades its
    /// assignments and their payments, but the structure FKs are ON DELETE
    /// CASCADE from Enrollments/Users below, so the order matters: payments and
    /// structures first, then the throwaway class and its students.
    /// </summary>
    private static async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync("DELETE FROM FeeStructures WHERE Name LIKE 'FE-%'");
        await conn.ExecuteAsync(@"
            DELETE FROM Enrollments
             WHERE ClassId IN (SELECT Id FROM Classes WHERE Name LIKE 'FE-%')");
        await conn.ExecuteAsync("DELETE FROM Classes WHERE Name LIKE 'FE-%'");
        await conn.ExecuteAsync("DELETE FROM Users WHERE Username LIKE 'fe_stu_%'");
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>A throwaway class with a section and its own students enrolled.</summary>
    private static async Task<(int ClassId, List<int> Students)> MakeClassAsync(
        string name = "FE-Alpha", int studentCount = 2)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        var classId = await conn.QuerySingleAsync<int>(
            "INSERT INTO Classes (Name) VALUES (@n) RETURNING Id", new { n = name });
        var sectionId = await conn.QuerySingleAsync<int>(
            "INSERT INTO Sections (Name, ClassId) VALUES ('A', @c) RETURNING Id", new { c = classId });

        var students = new List<int>();
        for (var i = 0; i < studentCount; i++)
        {
            var tag = name.Replace("FE-", "").ToLowerInvariant();
            var username = $"fe_stu_{tag}_{i}";
            var userId = await conn.QuerySingleAsync<int>(@"
                INSERT INTO Users (Username, Email, PasswordHash, IsActive, Role)
                VALUES (@u, @e, 'not-a-real-hash', true, 'Student')
                RETURNING Id", new { u = username, e = $"{username}@test.local" });
            var studentId = await conn.QuerySingleAsync<int>(@"
                INSERT INTO Students (UserId, RollNumber)
                VALUES (@u, @r) RETURNING Id",
                new { u = userId, r = $"FE-{tag}-{i}" });

            await conn.ExecuteAsync(@"
                INSERT INTO Enrollments (StudentId, ClassId, SectionId)
                VALUES (@s, @c, @sec)", new { s = studentId, c = classId, sec = sectionId });
            students.Add(studentId);
        }

        return (classId, students);
    }

    /// <summary>Creates a fee structure and returns its id.</summary>
    private async Task<int> MakeStructureAsync(string name, decimal amount, int? classId = null)
    {
        using var res = await _admin.PostAsJsonAsync("/api/fees/structures",
            new { Name = name, Amount = amount, ClassId = classId });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("FeeStructureId").GetInt32();
    }

    private async Task<int> AssignAsync(int feeStructureId, int? studentId, int? classId = null,
        DateTime? dueDate = null)
    {
        using var res = await _admin.PostAsJsonAsync("/api/fees/assignments", new
        {
            FeeStructureId = feeStructureId,
            StudentId = studentId,
            ClassId = classId,
            DueDate = dueDate ?? DateTime.UtcNow.Date.AddDays(30)
        });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("Assigned").GetInt32();
    }

    /// <summary>All assignments, optionally narrowed to one class's roster.</summary>
    private async Task<JsonElement> AssignmentsAsync(int? classId = null)
    {
        var url = classId is null ? "/api/fees/assignments" : $"/api/fees/assignments?classId={classId}";
        using var res = await _admin.GetAsync(url);
        res.EnsureSuccessStatusCode();
        return await ReadJson(res);
    }

    /// <summary>The single assignment for a fee assigned to one student.</summary>
    private async Task<(int Id, decimal Paid, decimal Outstanding, string Status)> AssignmentOfAsync(string feeName)
    {
        var rows = (await AssignmentsAsync()).EnumerateArray()
            .Where(r => r.GetProperty("FeeName").GetString() == feeName)
            .ToList();
        var row = Assert.Single(rows);
        return (row.GetProperty("Id").GetInt32(),
                row.GetProperty("Paid").GetDecimal(),
                row.GetProperty("Outstanding").GetDecimal(),
                row.GetProperty("Status").GetString()!);
    }

    /* --------------------------- structures --------------------------- */

    [Fact]
    public async Task FeeStructure_Rejects_Empty_Name_Zero_Amount_And_Duplicate_Name()
    {
        await ResetAsync();
        var name = $"FE-Term-{Guid.NewGuid():N}";

        using var noName = await _admin.PostAsJsonAsync("/api/fees/structures",
            new { Name = "  ", Amount = 100m });
        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);

        using var zero = await _admin.PostAsJsonAsync("/api/fees/structures",
            new { Name = name, Amount = 0m });
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);

        await MakeStructureAsync(name, 100m);

        using var duplicate = await _admin.PostAsJsonAsync("/api/fees/structures",
            new { Name = name, Amount = 200m });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Class_Scoped_Fee_Cannot_Be_Assigned_To_Another_Class()
    {
        await ResetAsync();
        var (classA, _) = await MakeClassAsync("FE-Alpha");
        var (classB, _) = await MakeClassAsync("FE-Beta");
        var structureId = await MakeStructureAsync($"FE-Scoped-{Guid.NewGuid():N}", 100m, classA);

        using var res = await _admin.PostAsJsonAsync("/api/fees/assignments", new
        {
            FeeStructureId = structureId,
            ClassId = classB,
            DueDate = DateTime.UtcNow.Date.AddDays(30)
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /* --------------------------- assignment --------------------------- */

    [Fact]
    public async Task Assigning_The_Same_Fee_Twice_Is_A_No_Op()
    {
        await ResetAsync();
        var (classId, students) = await MakeClassAsync("FE-Alpha", 3);
        var name = $"FE-Term-{Guid.NewGuid():N}";
        var structureId = await MakeStructureAsync(name, 500m);

        var first = await AssignAsync(structureId, null, classId);
        Assert.Equal(students.Count, first);

        var second = await AssignAsync(structureId, null, classId);
        Assert.Equal(0, second);

        // And the ledger still holds one row per student, not two.
        var mine = (await AssignmentsAsync(classId)).EnumerateArray()
            .Where(r => r.GetProperty("FeeName").GetString() == name)
            .ToList();
        Assert.Equal(students.Count, mine.Count);
        Assert.All(mine, r => Assert.Equal("Unpaid", r.GetProperty("Status").GetString()));
    }

    /* --------------------------- payments --------------------------- */

    [Fact]
    public async Task Payment_Moves_Status_Through_Unpaid_Partial_Paid_And_Refuses_Overpayment()
    {
        await ResetAsync();
        var (_, students) = await MakeClassAsync("FE-Alpha", 1);
        var studentId = students[0];
        var name = $"FE-Term-{Guid.NewGuid():N}";
        var structureId = await MakeStructureAsync(name, 100m);
        await AssignAsync(structureId, studentId);

        var (feeId, paid, outstanding, status) = await AssignmentOfAsync(name);
        Assert.Equal(0m, paid);
        Assert.Equal(100m, outstanding);
        Assert.Equal("Unpaid", status);

        using (var partial = await _admin.PostAsJsonAsync("/api/fees/payments",
            new { StudentFeeId = feeId, AmountPaid = 40m, PaymentMethod = "Cash" }))
        {
            partial.EnsureSuccessStatusCode();
            var body = await ReadJson(partial);
            Assert.Equal(40m, body.GetProperty("Paid").GetDecimal());
            Assert.Equal(60m, body.GetProperty("Outstanding").GetDecimal());
        }

        (_, paid, outstanding, status) = await AssignmentOfAsync(name);
        Assert.Equal(40m, paid);
        Assert.Equal(60m, outstanding);
        Assert.Equal("Partial", status);

        // More than is owed is refused and names the balance, rather than
        // silently becoming credit against a future fee.
        using (var tooMuch = await _admin.PostAsJsonAsync("/api/fees/payments",
            new { StudentFeeId = feeId, AmountPaid = 100m, PaymentMethod = "Cash" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
            Assert.Contains("60", await tooMuch.Content.ReadAsStringAsync());
        }

        using (var settle = await _admin.PostAsJsonAsync("/api/fees/payments",
            new { StudentFeeId = feeId, AmountPaid = 60m, PaymentMethod = "Card" }))
        {
            settle.EnsureSuccessStatusCode();
        }

        (_, paid, outstanding, status) = await AssignmentOfAsync(name);
        Assert.Equal(100m, paid);
        Assert.Equal(0m, outstanding);
        Assert.Equal("Paid", status);

        // Paying a settled fee is a conflict, not another payment.
        using var again = await _admin.PostAsJsonAsync("/api/fees/payments",
            new { StudentFeeId = feeId, AmountPaid = 1m, PaymentMethod = "Cash" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Overdue_Outranks_Partial_For_A_Fee_Past_Its_Due_Date()
    {
        await ResetAsync();
        var (_, students) = await MakeClassAsync("FE-Alpha", 1);
        var name = $"FE-Term-{Guid.NewGuid():N}";
        var structureId = await MakeStructureAsync(name, 100m);
        await AssignAsync(structureId, students[0], dueDate: DateTime.UtcNow.Date.AddDays(-10));

        var (feeId, _, _, status) = await AssignmentOfAsync(name);
        Assert.Equal("Overdue", status);

        // Part-paying an overdue fee stays Overdue rather than flipping to Partial.
        using var payment = await _admin.PostAsJsonAsync("/api/fees/payments",
            new { StudentFeeId = feeId, AmountPaid = 30m, PaymentMethod = "Cash" });
        payment.EnsureSuccessStatusCode();

        (_, _, _, status) = await AssignmentOfAsync(name);
        Assert.Equal("Overdue", status);
    }

    /* --------------------------- guards --------------------------- */

    [Fact]
    public async Task Collected_Money_Blocks_Removing_The_Assignment_And_The_Structure()
    {
        await ResetAsync();
        var (_, students) = await MakeClassAsync("FE-Alpha", 1);
        var name = $"FE-Term-{Guid.NewGuid():N}";
        var structureId = await MakeStructureAsync(name, 100m);
        await AssignAsync(structureId, students[0]);

        var (feeId, _, _, _) = await AssignmentOfAsync(name);
        using (var payment = await _admin.PostAsJsonAsync("/api/fees/payments",
            new { StudentFeeId = feeId, AmountPaid = 25m, PaymentMethod = "Cash" }))
        {
            payment.EnsureSuccessStatusCode();
        }

        using (var remove = await _admin.DeleteAsync($"/api/fees/assignments/{feeId}"))
            Assert.Equal(HttpStatusCode.Conflict, remove.StatusCode);

        using (var delete = await _admin.DeleteAsync($"/api/fees/structures/{structureId}"))
            Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);

        // The ledger row survives the refused deletes, payment intact.
        var (_, paid, _, _) = await AssignmentOfAsync(name);
        Assert.Equal(25m, paid);
    }

    [Fact]
    public async Task Unpaid_Assignment_And_Structure_Can_Be_Removed()
    {
        await ResetAsync();
        var (_, students) = await MakeClassAsync("FE-Alpha", 1);
        var name = $"FE-Term-{Guid.NewGuid():N}";
        var structureId = await MakeStructureAsync(name, 100m);
        await AssignAsync(structureId, students[0]);

        var (feeId, _, _, _) = await AssignmentOfAsync(name);

        using (var remove = await _admin.DeleteAsync($"/api/fees/assignments/{feeId}"))
            Assert.Equal(HttpStatusCode.OK, remove.StatusCode);

        using (var delete = await _admin.DeleteAsync($"/api/fees/structures/{structureId}"))
            Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
    }

    /* --------------------------- learner ledger --------------------------- */

    [Fact]
    public async Task Student_Ledger_Derives_Outstanding_And_Status()
    {
        await ResetAsync();
        var name = $"FE-Term-{Guid.NewGuid():N}";
        var structureId = await MakeStructureAsync(name, 250m);
        await AssignAsync(structureId, _fixture.LinkedStudentId);

        var (feeId, _, _, _) = await AssignmentOfAsync(name);
        using (var payment = await _admin.PostAsJsonAsync("/api/fees/payments",
            new { StudentFeeId = feeId, AmountPaid = 100m, PaymentMethod = "Bank Transfer" }))
        {
            payment.EnsureSuccessStatusCode();
        }

        using var res = await _student.GetAsync($"/api/students/{_fixture.LinkedStudentId}/fees");
        res.EnsureSuccessStatusCode();

        var row = (await ReadJson(res)).EnumerateArray()
            .Single(r => r.GetProperty("FeeName").GetString() == name);
        Assert.Equal(250m, row.GetProperty("Amount").GetDecimal());
        Assert.Equal(100m, row.GetProperty("Paid").GetDecimal());
        Assert.Equal(150m, row.GetProperty("Outstanding").GetDecimal());
        Assert.Equal("Partial", row.GetProperty("Status").GetString());
    }

    /* --------------------------- report --------------------------- */

    [Fact]
    public async Task Fee_Collection_Report_Groups_By_Derived_Status()
    {
        await ResetAsync();
        var (classId, students) = await MakeClassAsync("FE-Report", 2);
        var name = $"FE-Term-{Guid.NewGuid():N}";
        var structureId = await MakeStructureAsync(name, 100m);
        await AssignAsync(structureId, null, classId);

        var mine = (await AssignmentsAsync(classId)).EnumerateArray()
            .Where(r => r.GetProperty("FeeName").GetString() == name)
            .ToList();
        Assert.Equal(students.Count, mine.Count);

        // Settle one of the two in full; the other stays unpaid.
        using (var payment = await _admin.PostAsJsonAsync("/api/fees/payments",
            new { StudentFeeId = mine[0].GetProperty("Id").GetInt32(), AmountPaid = 100m, PaymentMethod = "Cash" }))
        {
            payment.EnsureSuccessStatusCode();
        }

        using var res = await _admin.GetAsync("/api/reports/fee-collection");
        res.EnsureSuccessStatusCode();
        var report = await ReadJson(res);

        var paid = report.EnumerateArray().Single(r => r.GetProperty("Status").GetString() == "Paid");
        Assert.Equal(1, paid.GetProperty("FeeCount").GetInt32());
        Assert.Equal(100m, paid.GetProperty("TotalPaid").GetDecimal());
        Assert.Equal(0m, paid.GetProperty("TotalOutstanding").GetDecimal());

        var unpaid = report.EnumerateArray().Single(r => r.GetProperty("Status").GetString() == "Unpaid");
        Assert.Equal(1, unpaid.GetProperty("FeeCount").GetInt32());
        Assert.Equal(100m, unpaid.GetProperty("TotalOutstanding").GetDecimal());
    }

    /* --------------------------- authorization --------------------------- */

    [Fact]
    public async Task Fee_Administration_Is_Admin_Only()
    {
        await ResetAsync();

        using (var teacherList = await _teacher.GetAsync("/api/fees/structures"))
            Assert.Equal(HttpStatusCode.Forbidden, teacherList.StatusCode);

        using (var studentAssignments = await _student.GetAsync("/api/fees/assignments"))
            Assert.Equal(HttpStatusCode.Forbidden, studentAssignments.StatusCode);

        using (var studentCreate = await _student.PostAsJsonAsync("/api/fees/structures",
            new { Name = "FE-Nope", Amount = 1m }))
            Assert.Equal(HttpStatusCode.Forbidden, studentCreate.StatusCode);
    }

    /* --------------------------- status derivation --------------------------- */

    [Theory]
    [InlineData(100, 0, "Unpaid")]
    [InlineData(100, 40, "Partial")]
    [InlineData(100, 100, "Paid")]
    [InlineData(100, 120, "Paid")]
    public void Derive_Reports_The_Expected_Status(decimal amount, decimal paid, string expected)
    {
        var due = DateTime.UtcNow.Date.AddDays(5);
        Assert.Equal(expected, FeesController.Derive(amount, paid, due));
    }

    [Fact]
    public void Derive_Reports_Overdue_When_A_Fee_Is_Unsettled_Past_Its_Due_Date()
    {
        var past = DateTime.UtcNow.Date.AddDays(-1);
        Assert.Equal("Overdue", FeesController.Derive(100m, 0m, past));
        Assert.Equal("Overdue", FeesController.Derive(100m, 40m, past));
        // A settled fee is Paid even if it was paid late.
        Assert.Equal("Paid", FeesController.Derive(100m, 100m, past));
    }
}
