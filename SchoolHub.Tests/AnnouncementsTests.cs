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
/// Covers the announcement flow.
///
/// Announcements could only be listed and created, and the list returned every
/// row to every role: a notice addressed to Parents was shown to students too.
/// There was no edit or delete, no author, and the ClassId column was never
/// written. What is worth protecting now:
///
///   1. A notice needs a non-blank title and body, a real audience, and a real
///      class when one is named; anything else is a 400.
///   2. It can be edited and deleted by its author or an Admin; a teacher cannot
///      touch someone else's.
///   3. A learner sees only the notices addressed to their role, and only their
///      own class's when a class is named; a parent sees their child's class.
///   4. Creating, editing and deleting is staff-only.
/// </summary>
[Collection(AuthTestCollection.Name)]
public class AnnouncementsTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Database=schoolhub_db;Username=postgres;Password=00000";

    private readonly AuthTestFixture _fixture;
    private readonly HttpClient _admin;
    private readonly HttpClient _teacher;
    private readonly HttpClient _student;
    private readonly HttpClient _parent;

    public AnnouncementsTests(AuthTestFixture fixture)
    {
        _fixture = fixture;
        _admin = AuthTestFixture.ClientFor(fixture, fixture.AdminToken);
        _teacher = AuthTestFixture.ClientFor(fixture, fixture.TeacherToken);
        _student = AuthTestFixture.ClientFor(fixture, fixture.StudentToken);
        _parent = AuthTestFixture.ClientFor(fixture, fixture.ParentToken);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _admin.Dispose();
        _teacher.Dispose();
        _student.Dispose();
        _parent.Dispose();
        await ResetAsync();
    }

    /// <summary>
    /// Removes every AN- throwaway row. Deleting a class cascades its sections,
    /// class-subject mappings and enrolments.
    /// </summary>
    private static async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM Announcements WHERE Title LIKE 'AN-%'");
        await conn.ExecuteAsync("DELETE FROM Classes WHERE Name LIKE 'AN-%'");
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static string NewTitle() => "AN-" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>A throwaway class with one section.</summary>
    private static async Task<(int ClassId, int SectionId)> MakeClassAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        var classId = await conn.QuerySingleAsync<int>(
            "INSERT INTO Classes (Name) VALUES (@n) RETURNING Id",
            new { n = "AN-Class-" + Guid.NewGuid().ToString("N")[..6] });
        var sectionId = await conn.QuerySingleAsync<int>(
            "INSERT INTO Sections (Name, ClassId) VALUES ('A', @c) RETURNING Id", new { c = classId });

        return (classId, sectionId);
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

    private async Task<int> CreateAsync(
        HttpClient client, string? targetRole = "All", int? classId = null, string? title = null)
    {
        using var res = await client.PostAsJsonAsync("/api/announcements", new
        {
            Title = title ?? NewTitle(),
            Content = "Throwaway test announcement body.",
            TargetRole = targetRole,
            ClassId = classId,
        });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("AnnouncementId").GetInt32();
    }

    private static async Task<bool> ListHasAsync(HttpClient client, int id)
    {
        using var res = await client.GetAsync("/api/announcements");
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).EnumerateArray()
            .Any(e => e.GetProperty("Id").GetInt32() == id);
    }

    /* --------------------------- validation --------------------------- */

    [Fact]
    public async Task Create_Rejects_Blank_Title_Blank_Body_Bad_Audience_And_Unknown_Class()
    {
        await ResetAsync();

        async Task<HttpStatusCode> PostAsync(object body)
        {
            using var res = await _admin.PostAsJsonAsync("/api/announcements", body);
            return res.StatusCode;
        }

        var body = "A body that says something.";
        Assert.Equal(HttpStatusCode.BadRequest,
            await PostAsync(new { Title = "   ", Content = body, TargetRole = "All" }));
        Assert.Equal(HttpStatusCode.BadRequest,
            await PostAsync(new { Title = NewTitle(), Content = "  ", TargetRole = "All" }));
        Assert.Equal(HttpStatusCode.BadRequest,
            await PostAsync(new { Title = NewTitle(), Content = body, TargetRole = "Everyone" }));
        Assert.Equal(HttpStatusCode.BadRequest,
            await PostAsync(new { Title = NewTitle(), Content = body, TargetRole = "All", ClassId = 999_999 }));
    }

    [Fact]
    public async Task Announcement_Can_Be_Updated_And_Deleted()
    {
        await ResetAsync();
        var id = await CreateAsync(_admin);

        var renamed = NewTitle();
        using (var update = await _admin.PutAsJsonAsync($"/api/announcements/{id}", new
        {
            Title = renamed,
            Content = "Edited body.",
            TargetRole = "All",
            ClassId = (int?)null,
        }))
            update.EnsureSuccessStatusCode();

        using (var fetch = await _student.GetAsync($"/api/announcements/{id}"))
        {
            fetch.EnsureSuccessStatusCode();
            var item = await ReadJson(fetch);
            Assert.Equal(renamed, item.GetProperty("Title").GetString());
            Assert.Equal("Edited body.", item.GetProperty("Content").GetString());
            Assert.Equal(AuthTestFixture.AdminUsername, item.GetProperty("AuthorName").GetString());
        }

        using (var delete = await _admin.DeleteAsync($"/api/announcements/{id}"))
            delete.EnsureSuccessStatusCode();

        using (var gone = await _admin.GetAsync($"/api/announcements/{id}"))
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Unknown_Announcement_Returns_NotFound()
    {
        await ResetAsync();
        const int missing = 999_999;

        using (var get = await _admin.GetAsync($"/api/announcements/{missing}"))
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        using (var update = await _admin.PutAsJsonAsync($"/api/announcements/{missing}",
            new { Title = NewTitle(), Content = "Body.", TargetRole = "All" }))
            Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        using (var delete = await _admin.DeleteAsync($"/api/announcements/{missing}"))
            Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    /* --------------------------- audience scoping --------------------------- */

    [Fact]
    public async Task Learner_And_Parent_See_Only_Their_Audience()
    {
        await ResetAsync();
        var all = await CreateAsync(_admin, "All");
        var toStudents = await CreateAsync(_admin, "Student");
        var toParents = await CreateAsync(_admin, "Parent");
        var toTeachers = await CreateAsync(_admin, "Teacher");

        Assert.True(await ListHasAsync(_student, all));
        Assert.True(await ListHasAsync(_student, toStudents));
        Assert.False(await ListHasAsync(_student, toParents));
        Assert.False(await ListHasAsync(_student, toTeachers));

        Assert.True(await ListHasAsync(_parent, all));
        Assert.True(await ListHasAsync(_parent, toParents));
        Assert.False(await ListHasAsync(_parent, toStudents));
        Assert.False(await ListHasAsync(_parent, toTeachers));

        // Staff moderate communications and see every notice.
        foreach (var id in new[] { all, toStudents, toParents, toTeachers })
        {
            Assert.True(await ListHasAsync(_admin, id));
            Assert.True(await ListHasAsync(_teacher, id));
        }
    }

    [Fact]
    public async Task Class_Scoped_Announcement_Reaches_Only_That_Class()
    {
        await ResetAsync();
        var (classA, sectionA) = await MakeClassAsync();
        var (classB, _) = await MakeClassAsync();
        await EnrollLinkedStudentAsync(classA, sectionA);

        var inMyClass = await CreateAsync(_admin, "All", classA);
        var otherClass = await CreateAsync(_admin, "All", classB);

        // The enrolled learner and their linked parent see their own class's
        // notice and not the other class's.
        Assert.True(await ListHasAsync(_student, inMyClass));
        Assert.False(await ListHasAsync(_student, otherClass));
        Assert.True(await ListHasAsync(_parent, inMyClass));
        Assert.False(await ListHasAsync(_parent, otherClass));

        // And the single read is scoped the same way, as a 404 rather than a 403.
        using (var hidden = await _student.GetAsync($"/api/announcements/{otherClass}"))
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using (var visible = await _student.GetAsync($"/api/announcements/{inMyClass}"))
            visible.EnsureSuccessStatusCode();
    }

    /* --------------------------- ownership --------------------------- */

    [Fact]
    public async Task Teacher_Manages_Only_Their_Own_Announcements()
    {
        await ResetAsync();
        var mine = await CreateAsync(_teacher);
        var admins = await CreateAsync(_admin);

        using (var editMine = await _teacher.PutAsJsonAsync($"/api/announcements/{mine}", new
        {
            Title = NewTitle(),
            Content = "Teacher edit.",
            TargetRole = "Student",
        }))
            editMine.EnsureSuccessStatusCode();

        using (var editTheirs = await _teacher.PutAsJsonAsync($"/api/announcements/{admins}", new
        {
            Title = NewTitle(),
            Content = "Should be refused.",
            TargetRole = "All",
        }))
            Assert.Equal(HttpStatusCode.Forbidden, editTheirs.StatusCode);

        using (var deleteTheirs = await _teacher.DeleteAsync($"/api/announcements/{admins}"))
            Assert.Equal(HttpStatusCode.Forbidden, deleteTheirs.StatusCode);

        using (var deleteMine = await _teacher.DeleteAsync($"/api/announcements/{mine}"))
            deleteMine.EnsureSuccessStatusCode();

        using (var adminClears = await _admin.DeleteAsync($"/api/announcements/{admins}"))
            adminClears.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Announcement_Writes_Are_Staff_Only()
    {
        await ResetAsync();
        var id = await CreateAsync(_admin);

        using (var create = await _student.PostAsJsonAsync("/api/announcements",
            new { Title = NewTitle(), Content = "Body.", TargetRole = "All" }))
            Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        using (var update = await _student.PutAsJsonAsync($"/api/announcements/{id}",
            new { Title = NewTitle(), Content = "Body.", TargetRole = "All" }))
            Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);

        using (var delete = await _student.DeleteAsync($"/api/announcements/{id}"))
            Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }
}
