using Dapper;
using Npgsql;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace SchoolHub.Tests;

/// <summary>
/// Covers the assignment flow.
///
/// The controller could create an assignment and accept a submission, and that
/// was all. There was no edit or delete, no way to read or grade what was handed
/// in, and the list returned every assignment in the database to every student -
/// a Grade 8 learner saw Grade 10's work. A submission check-then-insert also
/// allowed a duplicate row for the same student and assignment. What is worth
/// protecting now:
///
///   1. An assignment needs a title, a real subject, a due date and a positive
///      maximum; anything else is a 400.
///   2. It can be edited and deleted by the teacher who owns it, or an Admin;
///      a teacher cannot touch another teacher's (or an unowned) assignment.
///   3. A learner reads only the assignments their class offers, and can submit
///      only to those; a second submission replaces the first rather than
///      adding a row.
///   4. Staff read submissions and grade within the assignment's maximum; the
///      learner sees the score and feedback back on the list.
/// </summary>
[Collection(AuthTestCollection.Name)]
public class AssignmentsTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Database=schoolhub_db;Username=postgres;Password=00000";

    private readonly AuthTestFixture _fixture;
    private readonly HttpClient _admin;
    private readonly HttpClient _teacher;
    private readonly HttpClient _student;

    public AssignmentsTests(AuthTestFixture fixture)
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
    /// Removes every AS- throwaway row. Deleting a class cascades its sections,
    /// enrolments and class-subject mappings; deleting a subject cascades its
    /// assignments and their submissions.
    /// </summary>
    private static async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM Classes WHERE Name LIKE 'AS-%'");
        await conn.ExecuteAsync("DELETE FROM Subjects WHERE Code LIKE 'AS-%'");
        await conn.ExecuteAsync("DELETE FROM Assignments WHERE Title LIKE 'AS-%'");
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static string NewName(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6];

    /// <summary>A throwaway class with a section and one subject it offers.</summary>
    private static async Task<(int ClassId, int SectionId, int SubjectId)> MakeClassAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        var classId = await conn.QuerySingleAsync<int>(
            "INSERT INTO Classes (Name) VALUES (@n) RETURNING Id", new { n = NewName("AS-Class-") });
        var sectionId = await conn.QuerySingleAsync<int>(
            "INSERT INTO Sections (Name, ClassId) VALUES ('A', @c) RETURNING Id", new { c = classId });

        var subjectId = await conn.QuerySingleAsync<int>(
            "INSERT INTO Subjects (Name, Code) VALUES (@n, @code) RETURNING Id",
            new { n = NewName("AS-Subject-"), code = "AS-" + Guid.NewGuid().ToString("N")[..8] });
        await conn.ExecuteAsync(
            "INSERT INTO ClassSubjects (ClassId, SubjectId) VALUES (@c, @s)",
            new { c = classId, s = subjectId });

        return (classId, sectionId, subjectId);
    }

    /// <summary>A subject that exists but is not offered by any throwaway class.</summary>
    private static async Task<int> MakeLooseSubjectAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        return await conn.QuerySingleAsync<int>(
            "INSERT INTO Subjects (Name, Code) VALUES (@n, @code) RETURNING Id",
            new { n = NewName("AS-Loose-"), code = "AS-" + Guid.NewGuid().ToString("N")[..8] });
    }

    private async Task EnrollLinkedStudentAsync(int classId, int sectionId)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO Enrollments (StudentId, ClassId, SectionId)
            VALUES (@s, @c, @sec)
            ON CONFLICT (StudentId) DO UPDATE SET ClassId = @c, SectionId = @sec",
            new { s = _fixture.LinkedStudentId, c = classId, sec = sectionId });
    }

    private static async Task<int> CreateAssignmentAsync(
        HttpClient client, int subjectId, string title, decimal maxScore = 50)
    {
        using var res = await client.PostAsJsonAsync("/api/assignments", new
        {
            SubjectId = subjectId,
            Title = title,
            Description = "Throwaway test assignment",
            DueDate = DateTime.UtcNow.AddDays(7),
            MaxScore = maxScore,
            AttachmentUrl = (string?)null,
        });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("AssignmentId").GetInt32();
    }

    private static async Task<bool> ListHasAsync(HttpClient client, int assignmentId)
    {
        using var res = await client.GetAsync("/api/assignments");
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).EnumerateArray()
            .Any(e => e.GetProperty("Id").GetInt32() == assignmentId);
    }

    /* --------------------------- validation --------------------------- */

    [Fact]
    public async Task Create_Rejects_Blank_Title_Missing_Subject_Zero_Maxscore_And_Unknown_Subject()
    {
        await ResetAsync();
        var subjectId = await MakeLooseSubjectAsync();

        async Task<HttpStatusCode> PostAsync(object body)
        {
            using var res = await _admin.PostAsJsonAsync("/api/assignments", body);
            return res.StatusCode;
        }

        Assert.Equal(HttpStatusCode.BadRequest,
            await PostAsync(new { SubjectId = subjectId, Title = "  ", DueDate = DateTime.UtcNow.AddDays(1), MaxScore = 10m }));
        Assert.Equal(HttpStatusCode.BadRequest,
            await PostAsync(new { SubjectId = 0, Title = NewName("AS-"), DueDate = DateTime.UtcNow.AddDays(1), MaxScore = 10m }));
        Assert.Equal(HttpStatusCode.BadRequest,
            await PostAsync(new { SubjectId = subjectId, Title = NewName("AS-"), DueDate = default(DateTime), MaxScore = 10m }));
        Assert.Equal(HttpStatusCode.BadRequest,
            await PostAsync(new { SubjectId = subjectId, Title = NewName("AS-"), DueDate = DateTime.UtcNow.AddDays(1), MaxScore = 0m }));
        Assert.Equal(HttpStatusCode.BadRequest,
            await PostAsync(new { SubjectId = 999_999, Title = NewName("AS-"), DueDate = DateTime.UtcNow.AddDays(1), MaxScore = 10m }));
    }

    [Fact]
    public async Task Unknown_Assignment_Returns_NotFound()
    {
        await ResetAsync();
        const int missing = 999_999;

        using (var get = await _admin.GetAsync($"/api/assignments/{missing}"))
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        using (var update = await _admin.PutAsJsonAsync($"/api/assignments/{missing}",
            new { SubjectId = 1, Title = NewName("AS-"), DueDate = DateTime.UtcNow.AddDays(1), MaxScore = 10m }))
            Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        using (var subs = await _admin.GetAsync($"/api/assignments/{missing}/submissions"))
            Assert.Equal(HttpStatusCode.NotFound, subs.StatusCode);

        using (var delete = await _admin.DeleteAsync($"/api/assignments/{missing}"))
            Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    /* --------------------------- ownership --------------------------- */

    [Fact]
    public async Task Owner_Can_Edit_And_Delete_And_Admin_Can_Too()
    {
        await ResetAsync();
        var (_, _, subjectId) = await MakeClassAsync();
        var id = await CreateAssignmentAsync(_teacher, subjectId, NewName("AS-"));

        using (var owner = await _teacher.PutAsJsonAsync($"/api/assignments/{id}",
            new { SubjectId = subjectId, Title = NewName("AS-"), DueDate = DateTime.UtcNow.AddDays(3), MaxScore = 80m }))
            owner.EnsureSuccessStatusCode();

        using (var admin = await _admin.PutAsJsonAsync($"/api/assignments/{id}",
            new { SubjectId = subjectId, Title = NewName("AS-"), DueDate = DateTime.UtcNow.AddDays(4), MaxScore = 90m }))
            admin.EnsureSuccessStatusCode();

        using (var delete = await _teacher.DeleteAsync($"/api/assignments/{id}"))
            delete.EnsureSuccessStatusCode();

        using (var gone = await _admin.GetAsync($"/api/assignments/{id}"))
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Teacher_Cannot_Manage_An_Assignment_They_Do_Not_Own()
    {
        await ResetAsync();
        var (_, _, subjectId) = await MakeClassAsync();
        // An Admin creates this one, so it has no owning teacher.
        var id = await CreateAssignmentAsync(_admin, subjectId, NewName("AS-"));

        using (var update = await _teacher.PutAsJsonAsync($"/api/assignments/{id}",
            new { SubjectId = subjectId, Title = NewName("AS-"), DueDate = DateTime.UtcNow.AddDays(3), MaxScore = 10m }))
            Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);

        using (var subs = await _teacher.GetAsync($"/api/assignments/{id}/submissions"))
            Assert.Equal(HttpStatusCode.Forbidden, subs.StatusCode);

        using (var delete = await _teacher.DeleteAsync($"/api/assignments/{id}"))
            Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task Assignment_Writes_Reject_Students()
    {
        await ResetAsync();
        var (_, _, subjectId) = await MakeClassAsync();

        using (var create = await _student.PostAsJsonAsync("/api/assignments",
            new { SubjectId = subjectId, Title = NewName("AS-"), DueDate = DateTime.UtcNow.AddDays(1), MaxScore = 10m }))
            Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        var id = await CreateAssignmentAsync(_admin, subjectId, NewName("AS-"));
        using (var update = await _student.PutAsJsonAsync($"/api/assignments/{id}",
            new { SubjectId = subjectId, Title = NewName("AS-"), DueDate = DateTime.UtcNow.AddDays(1), MaxScore = 10m }))
            Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);

        using (var delete = await _student.DeleteAsync($"/api/assignments/{id}"))
            Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    /* --------------------------- learner scoping --------------------------- */

    [Fact]
    public async Task Student_Sees_Only_The_Assignments_Their_Class_Offers()
    {
        await ResetAsync();
        var (classId, sectionId, offeredSubject) = await MakeClassAsync();
        var looseSubject = await MakeLooseSubjectAsync();
        await EnrollLinkedStudentAsync(classId, sectionId);

        var visible = await CreateAssignmentAsync(_admin, offeredSubject, NewName("AS-Visible"));
        var hidden = await CreateAssignmentAsync(_admin, looseSubject, NewName("AS-Hidden"));

        Assert.True(await ListHasAsync(_student, visible));
        Assert.False(await ListHasAsync(_student, hidden));

        // Staff still see everything.
        Assert.True(await ListHasAsync(_admin, visible));
        Assert.True(await ListHasAsync(_admin, hidden));

        using (var peek = await _student.GetAsync($"/api/assignments/{hidden}"))
            Assert.Equal(HttpStatusCode.NotFound, peek.StatusCode);
    }

    [Fact]
    public async Task Student_Submits_And_A_Resubmission_Replaces_The_First()
    {
        await ResetAsync();
        var (classId, sectionId, offeredSubject) = await MakeClassAsync();
        await EnrollLinkedStudentAsync(classId, sectionId);
        var id = await CreateAssignmentAsync(_admin, offeredSubject, NewName("AS-"));

        using (var first = await _student.PostAsJsonAsync("/api/assignments/submit",
            new { AssignmentId = id, FilePath = "first-link.pdf" }))
            first.EnsureSuccessStatusCode();

        using (var list = await _student.GetAsync("/api/assignments"))
        {
            list.EnsureSuccessStatusCode();
            var mine = (await ReadJson(list)).EnumerateArray().Single(e => e.GetProperty("Id").GetInt32() == id);
            Assert.Equal("first-link.pdf", mine.GetProperty("MyFilePath").GetString());
        }

        using (var second = await _student.PostAsJsonAsync("/api/assignments/submit",
            new { AssignmentId = id, FilePath = "second-link.pdf" }))
            second.EnsureSuccessStatusCode();

        using (var submissions = await _admin.GetAsync($"/api/assignments/{id}/submissions"))
        {
            submissions.EnsureSuccessStatusCode();
            var rows = (await ReadJson(submissions)).EnumerateArray().ToList();
            var only = Assert.Single(rows);
            Assert.Equal("second-link.pdf", only.GetProperty("FilePath").GetString());
        }
    }

    [Fact]
    public async Task Student_Cannot_Submit_To_An_Assignment_Not_Set_For_Their_Class()
    {
        await ResetAsync();
        var (classId, sectionId, _) = await MakeClassAsync();
        var looseSubject = await MakeLooseSubjectAsync();
        await EnrollLinkedStudentAsync(classId, sectionId);
        var hidden = await CreateAssignmentAsync(_admin, looseSubject, NewName("AS-"));

        using var submit = await _student.PostAsJsonAsync("/api/assignments/submit",
            new { AssignmentId = hidden, FilePath = "link.pdf" });
        Assert.Equal(HttpStatusCode.BadRequest, submit.StatusCode);

        using var blank = await _student.PostAsJsonAsync("/api/assignments/submit",
            new { AssignmentId = hidden, FilePath = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
    }

    /* --------------------------- grading --------------------------- */

    [Fact]
    public async Task Staff_Grade_Within_Bounds_And_The_Learner_Sees_It()
    {
        await ResetAsync();
        var (classId, sectionId, offeredSubject) = await MakeClassAsync();
        await EnrollLinkedStudentAsync(classId, sectionId);
        var id = await CreateAssignmentAsync(_admin, offeredSubject, NewName("AS-"), maxScore: 50);

        using (var submit = await _student.PostAsJsonAsync("/api/assignments/submit",
            new { AssignmentId = id, FilePath = "essay.pdf" }))
            submit.EnsureSuccessStatusCode();

        int submissionId;
        using (var submissions = await _admin.GetAsync($"/api/assignments/{id}/submissions"))
        {
            submissions.EnsureSuccessStatusCode();
            submissionId = (await ReadJson(submissions)).EnumerateArray().Single().GetProperty("Id").GetInt32();
        }

        using (var over = await _admin.PutAsJsonAsync($"/api/assignments/submissions/{submissionId}",
            new { Score = 60m, Feedback = "Too high" }))
            Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);

        using (var negative = await _admin.PutAsJsonAsync($"/api/assignments/submissions/{submissionId}",
            new { Score = -1m, Feedback = "No" }))
            Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);

        using (var grade = await _admin.PutAsJsonAsync($"/api/assignments/submissions/{submissionId}",
            new { Score = 45m, Feedback = "Well argued" }))
            grade.EnsureSuccessStatusCode();

        using (var list = await _student.GetAsync("/api/assignments"))
        {
            list.EnsureSuccessStatusCode();
            var mine = (await ReadJson(list)).EnumerateArray().Single(e => e.GetProperty("Id").GetInt32() == id);
            Assert.Equal(45m, mine.GetProperty("MyScore").GetDecimal());
            Assert.Equal("Well argued", mine.GetProperty("MyFeedback").GetString());
        }

        // Grading again with no score clears it back to ungraded.
        using (var clear = await _admin.PutAsJsonAsync($"/api/assignments/submissions/{submissionId}",
            new { Score = (decimal?)null, Feedback = (string?)null }))
            clear.EnsureSuccessStatusCode();

        using (var list = await _student.GetAsync("/api/assignments"))
        {
            list.EnsureSuccessStatusCode();
            var mine = (await ReadJson(list)).EnumerateArray().Single(e => e.GetProperty("Id").GetInt32() == id);
            Assert.Equal(JsonValueKind.Null, mine.GetProperty("MyScore").ValueKind);
        }
    }
}
