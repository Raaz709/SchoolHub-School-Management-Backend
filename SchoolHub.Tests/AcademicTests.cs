using Dapper;
using Npgsql;
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
/// Covers the academic setup endpoints behind the Academics page.
///
/// The two behaviours worth protecting are the guards: delete must refuse when
/// something still references the row, because Classes cascades to Sections and
/// Enrollments, and a silent cascade would strip students of their class. And
/// names must stay unique now that migrations/004 added ux_classes_name and
/// ux_sections_class_name.
/// </summary>
[Collection(AuthTestCollection.Name)]
public class AcademicTests
{
    private const string ConnectionString =
        "Host=localhost;Database=schoolhub_db;Username=postgres;Password=00000";

    private readonly AuthTestFixture _fixture;

    public AcademicTests(AuthTestFixture fixture) => _fixture = fixture;

    private HttpClient Admin => AuthTestFixture.ClientFor(_fixture, _fixture.AdminToken);
    private HttpClient Teacher => AuthTestFixture.ClientFor(_fixture, _fixture.TeacherToken);

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>Removes anything a previous interrupted run left behind.</summary>
    private static async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        // Order matters: children before parents.
        await conn.ExecuteAsync("DELETE FROM classsubjects WHERE classid IN (SELECT id FROM classes WHERE name LIKE 'T-%')");
        await conn.ExecuteAsync("DELETE FROM sections WHERE classid IN (SELECT id FROM classes WHERE name LIKE 'T-%')");
        await conn.ExecuteAsync("DELETE FROM classes WHERE name LIKE 'T-%'");
        await conn.ExecuteAsync("DELETE FROM subjects WHERE code LIKE 'T%'");
    }

    private async Task<(int ClassId, string Name)> CreateClassAsync(string name)
    {
        using var client = Admin;
        using var response = await client.PostAsJsonAsync("/api/academic/classes", new { Name = name });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJson(response);
        return (body.GetProperty("ClassId").GetInt32(), name);
    }

    /* ------------------------- read ------------------------- */

    [Fact]
    public async Task Classes_Report_A_Section_Count()
    {
        await ResetAsync();
        var (classId, _) = await CreateClassAsync("T-Gold");

        using var client = Admin;
        using var response = await client.GetAsync("/api/academic/classes");
        response.EnsureSuccessStatusCode();

        var rows = (await ReadJson(response)).EnumerateArray().ToList();
        var mine = rows.Single(c => c.GetProperty("Id").GetInt32() == classId);

        Assert.Equal(JsonValueKind.Number, mine.GetProperty("SectionCount").ValueKind);
        Assert.Equal(0, mine.GetProperty("SectionCount").GetInt32());
    }

    [Fact]
    public async Task Sections_Expose_Their_Class()
    {
        await ResetAsync();
        var (classId, name) = await CreateClassAsync("T-Silver");

        using var client = Admin;
        using var created = await client.PostAsJsonAsync(
            "/api/academic/sections", new { Name = "A", ClassId = classId });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        using var response = await client.GetAsync("/api/academic/sections");
        response.EnsureSuccessStatusCode();

        var rows = (await ReadJson(response)).EnumerateArray().ToList();
        var mine = rows.Single(s => s.GetProperty("ClassId").GetInt32() == classId);

        Assert.Equal(name, mine.GetProperty("ClassName").GetString());
    }

    [Fact]
    public async Task Subjects_Expose_The_Owning_Teacher()
    {
        await ResetAsync();
        using var client = Admin;
        using var response = await client.GetAsync("/api/academic/subjects");
        response.EnsureSuccessStatusCode();

        var rows = (await ReadJson(response)).EnumerateArray().ToList();
        Assert.NotEmpty(rows);
        foreach (var s in rows)
        {
            // TeacherName must always be present, null when unassigned. Its
            // absence would throw on the teacher module's "owner" column.
            Assert.Contains(s.GetProperty("TeacherName").ValueKind, new[] { JsonValueKind.String, JsonValueKind.Null });
        }
    }

    [Fact]
    public async Task An_Assigned_Subject_Reports_Its_Teacher_Name()
    {
        await ResetAsync();

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        // Subjects.TeacherId holds a Teachers.Id, not a Users.Id. Use a real
        // one so the Teachers -> Users join is exercised against actual data
        // instead of a fabricated id that would silently resolve to null.
        var teachers = (await conn.QueryAsync<(int TeacherId, string Username)>(
            """
            SELECT t.Id AS TeacherId, u.Username
              FROM Teachers t
              JOIN Users u ON u.Id = t.UserId
             LIMIT 1
            """)).ToList();
        Assert.True(teachers.Count > 0, "No teacher exists to assign the subject to.");
        var teacher = teachers[0];

        var subjectId = await conn.QuerySingleAsync<int>(
            "INSERT INTO Subjects (Name, Code, TeacherId) VALUES ('T-Owned', 'TOWNED1', @t) RETURNING Id",
            new { t = teacher.TeacherId });

        try
        {
            using var client = Admin;
            using var response = await client.GetAsync("/api/academic/subjects");
            response.EnsureSuccessStatusCode();

            var rows = (await ReadJson(response)).EnumerateArray().ToList();
            var owned = rows.Single(s => s.GetProperty("Code").GetString() == "TOWNED1");

            Assert.Equal(teacher.Username, owned.GetProperty("TeacherName").GetString());
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM subjects WHERE id = @s", new { s = subjectId });
        }
    }

    /* ------------------------- create ------------------------- */

    [Fact]
    public async Task Duplicate_Class_Name_Returns_400_Naming_The_Conflict()
    {
        await ResetAsync();
        await CreateClassAsync("T-Gold");

        using var client = Admin;
        using var response = await client.PostAsJsonAsync("/api/academic/classes", new { Name = "T-Gold" });

        // The unique index alone would surface as a bare 409 from the
        // middleware with no usable message.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadJson(response);
        Assert.Contains("already exists", body.GetProperty("Message").GetString()!);
    }

    [Fact]
    public async Task Blank_Class_Name_Returns_400()
    {
        using var client = Admin;
        using var response = await client.PostAsJsonAsync("/api/academic/classes", new { Name = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Section_Names_May_Repeat_Across_Classes()
    {
        // Unique per class, not globally: "A" is valid in every grade.
        await ResetAsync();
        var (first, _) = await CreateClassAsync("T-One");
        var (second, _) = await CreateClassAsync("T-Two");

        using var client = Admin;
        using var a = await client.PostAsJsonAsync("/api/academic/sections", new { Name = "A", ClassId = first });
        using var b = await client.PostAsJsonAsync("/api/academic/sections", new { Name = "A", ClassId = second });

        Assert.Equal(HttpStatusCode.OK, a.StatusCode);
        Assert.Equal(HttpStatusCode.OK, b.StatusCode);
    }

    [Fact]
    public async Task Duplicate_Section_In_The_Same_Class_Returns_400()
    {
        await ResetAsync();
        var (classId, _) = await CreateClassAsync("T-Three");

        using var client = Admin;
        using var first = await client.PostAsJsonAsync("/api/academic/sections", new { Name = "B", ClassId = classId });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await client.PostAsJsonAsync("/api/academic/sections", new { Name = "B", ClassId = classId });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Section_For_An_Unknown_Class_Returns_404()
    {
        using var client = Admin;
        using var response = await client.PostAsJsonAsync("/api/academic/sections", new { Name = "A", ClassId = 999999 });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_Subject_Code_Returns_400_Not_500()
    {
        await ResetAsync();
        using var client = Admin;

        using var first = await client.PostAsJsonAsync("/api/academic/subjects", new { Name = "T-Algebra", Code = "TMATH1" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await client.PostAsJsonAsync("/api/academic/subjects", new { Name = "T-Other", Code = "tmath1" });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    /* ------------------------- rename ------------------------- */

    [Fact]
    public async Task Admin_Can_Rename_A_Class()
    {
        await ResetAsync();
        var (classId, _) = await CreateClassAsync("T-Old");

        using var client = Admin;
        using var response = await client.PutAsJsonAsync($"/api/academic/classes/{classId}", new { Name = "T-New" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var check = await client.GetAsync("/api/academic/classes");
        var rows = (await ReadJson(check)).EnumerateArray().ToList();
        Assert.Equal("T-New", rows.Single(c => c.GetProperty("Id").GetInt32() == classId).GetProperty("Name").GetString());
    }

    [Fact]
    public async Task Renaming_A_Class_To_An_Existing_Name_Returns_400()
    {
        await ResetAsync();
        await CreateClassAsync("T-Taken");
        var (classId, _) = await CreateClassAsync("T-Mine");

        using var client = Admin;
        using var response = await client.PutAsJsonAsync($"/api/academic/classes/{classId}", new { Name = "T-Taken" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Renaming_A_Class_To_Its_Own_Name_Succeeds()
    {
        // exceptId must exclude the row being renamed, or saving an unchanged
        // name would report a conflict with itself.
        await ResetAsync();
        var (classId, name) = await CreateClassAsync("T-Same");

        using var client = Admin;
        using var response = await client.PutAsJsonAsync($"/api/academic/classes/{classId}", new { Name = name });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Renaming_An_Unknown_Class_Returns_404()
    {
        using var client = Admin;
        using var response = await client.PutAsJsonAsync("/api/academic/classes/999999", new { Name = "T-Nope" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /* ------------------------- guarded delete ------------------------- */

    [Fact]
    public async Task Deleting_An_Empty_Class_Succeeds_And_Takes_Its_Sections()
    {
        await ResetAsync();
        var (classId, name) = await CreateClassAsync("T-Vanish");

        using var client = Admin;
        using var section = await client.PostAsJsonAsync("/api/academic/sections", new { Name = "A", ClassId = classId });
        var sectionId = (await ReadJson(section)).GetProperty("SectionId").GetInt32();

        using var response = await client.DeleteAsync($"/api/academic/classes/{classId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(name, (await ReadJson(response)).GetProperty("Message").GetString()!);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM sections WHERE id = @id", new { id = sectionId }));
    }

    [Fact]
    public async Task Deleting_A_Class_With_Students_Returns_409_And_Changes_Nothing()
    {
        // The whole point of the guard: Classes cascades to Enrollments, so an
        // unguarded delete would silently unassign every student.
        await ResetAsync();
        var (classId, name) = await CreateClassAsync("T-Occupied");

        // Enroll a fixture student rather than creating one, so no cleanup needed.
        await using (var conn = new NpgsqlConnection(ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT INTO enrollments (studentid, classid) VALUES (@sid, @cid)",
                new { sid = _fixture.LinkedStudentId, cid = classId });
        }

        using var client = Admin;
        using var response = await client.DeleteAsync($"/api/academic/classes/{classId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadJson(response);
        Assert.Contains("student", body.GetProperty("Message").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(name, body.GetProperty("Message").GetString()!);

        await using var check = new NpgsqlConnection(ConnectionString);
        await check.OpenAsync();
        Assert.Equal(1, await check.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM enrollments WHERE classid = @cid", new { cid = classId }));
        Assert.Equal(1, await check.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM classes WHERE id = @cid", new { cid = classId }));

        // Leave the enrolment table as the fixture expects it.
        await using var fix = new NpgsqlConnection(ConnectionString);
        await fix.OpenAsync();
        await fix.ExecuteAsync(
            "DELETE FROM enrollments WHERE classid = @cid", new { cid = classId });
    }

    [Fact]
    public async Task Deleting_A_Section_With_Students_Returns_409()
    {
        await ResetAsync();
        var (classId, _) = await CreateClassAsync("T-Busy");

        using var client = Admin;
        using var section = await client.PostAsJsonAsync("/api/academic/sections", new { Name = "A", ClassId = classId });
        var sectionId = (await ReadJson(section)).GetProperty("SectionId").GetInt32();

        await using (var conn = new NpgsqlConnection(ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT INTO enrollments (studentid, classid, sectionid) VALUES (@sid, @cid, @sid2)",
                new { sid = _fixture.LinkedStudentId, cid = classId, sid2 = sectionId });
        }

        using var response = await client.DeleteAsync($"/api/academic/sections/{sectionId}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var fix = new NpgsqlConnection(ConnectionString);
        await fix.OpenAsync();
        await fix.ExecuteAsync("DELETE FROM enrollments WHERE sectionid = @id", new { id = sectionId });
    }

    [Fact]
    public async Task Moving_A_Populated_Section_To_Another_Class_Returns_409()
    {
        // Enrollments carry both ClassId and SectionId. Repointing the section
        // alone would leave those rows claiming a class the section is not in.
        await ResetAsync();
        var (from, _) = await CreateClassAsync("T-From");
        var (to, _) = await CreateClassAsync("T-To");

        using var client = Admin;
        using var section = await client.PostAsJsonAsync("/api/academic/sections", new { Name = "A", ClassId = from });
        var sectionId = (await ReadJson(section)).GetProperty("SectionId").GetInt32();

        await using (var conn = new NpgsqlConnection(ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT INTO enrollments (studentid, classid, sectionid) VALUES (@sid, @cid, @sid2)",
                new { sid = _fixture.LinkedStudentId, cid = from, sid2 = sectionId });
        }

        using var response = await client.PutAsJsonAsync(
            $"/api/academic/sections/{sectionId}", new { Name = "A", ClassId = to });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var fix = new NpgsqlConnection(ConnectionString);
        await fix.OpenAsync();
        await fix.ExecuteAsync("DELETE FROM enrollments WHERE sectionid = @id", new { id = sectionId });
    }

    [Fact]
    public async Task Deleting_An_Unknown_Item_Returns_404()
    {
        using var client = Admin;
        using var a = await client.DeleteAsync("/api/academic/classes/999999");
        using var b = await client.DeleteAsync("/api/academic/sections/999999");
        using var c = await client.DeleteAsync("/api/academic/subjects/999999");

        Assert.Equal(HttpStatusCode.NotFound, a.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, b.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, c.StatusCode);
    }

    /* --------------------- class subject mapping --------------------- */

    [Fact]
    public async Task Class_Subjects_Can_Be_Set_And_Replaced()
    {
        await ResetAsync();
        var (classId, name) = await CreateClassAsync("T-Mapped");

        using var client = Admin;
        using var created = await client.PostAsJsonAsync("/api/academic/subjects", new { Name = "T-Geography", Code = "TGEO1" });
        var subjectId = (await ReadJson(created)).GetProperty("SubjectId").GetInt32();

        using (var first = await client.PutAsJsonAsync(
            $"/api/academic/classes/{classId}/subjects", new { SubjectIdList = new[] { subjectId } }))
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using (var read = await client.GetAsync($"/api/academic/classes/{classId}/subjects"))
        {
            read.EnsureSuccessStatusCode();
            var rows = (await ReadJson(read)).EnumerateArray().ToList();
            Assert.Equal(1, rows.Count);
            Assert.Equal(subjectId, rows[0].GetProperty("Id").GetInt32());
        }

        // Re-saving replaces the list rather than accumulating it, so a retry
        // cannot duplicate rows.
        using (var second = await client.PutAsJsonAsync(
            $"/api/academic/classes/{classId}/subjects", new { SubjectIdList = new[] { subjectId, subjectId } }))
        {
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            var body = await ReadJson(second);
            Assert.Equal(1, body.GetProperty("Assigned").GetInt32());
        }

        using (var empty = await client.PutAsJsonAsync(
            $"/api/academic/classes/{classId}/subjects", new { SubjectIdList = Array.Empty<int>() }))
        {
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        }

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM classsubjects WHERE classid = @cid", new { cid = classId }));
    }

    [Fact]
    public async Task Assigning_An_Unknown_Subject_Returns_400_Naming_The_Id()
    {
        await ResetAsync();
        var (classId, _) = await CreateClassAsync("T-BadMap");

        using var client = Admin;
        using var response = await client.PutAsJsonAsync(
            $"/api/academic/classes/{classId}/subjects", new { SubjectIdList = new[] { 999999 } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("999999", (await ReadJson(response)).GetProperty("Message").GetString()!);
    }

    [Fact]
    public async Task Subjects_For_An_Unknown_Class_Returns_404()
    {
        using var client = Admin;
        using var response = await client.GetAsync("/api/academic/classes/999999/subjects");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_Mapped_Subject_Cannot_Be_Deleted()
    {
        await ResetAsync();
        var (classId, _) = await CreateClassAsync("T-Holds");

        using var client = Admin;
        using var created = await client.PostAsJsonAsync("/api/academic/subjects", new { Name = "T-Locked", Code = "TLOCK1" });
        var subjectId = (await ReadJson(created)).GetProperty("SubjectId").GetInt32();

        using (var map = await client.PutAsJsonAsync(
            $"/api/academic/classes/{classId}/subjects", new { SubjectIdList = new[] { subjectId } }))
            Assert.Equal(HttpStatusCode.OK, map.StatusCode);

        using var response = await client.DeleteAsync($"/api/academic/subjects/{subjectId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("class subject mapping", (await ReadJson(response)).GetProperty("Message").GetString()!);
    }

    /* ------------------------- role boundary ------------------------- */

    [Fact]
    public async Task Teacher_May_Read_But_May_Not_Write_Academic_Data()
    {
        await ResetAsync();
        using var client = Teacher;

        using var classes = await client.GetAsync("/api/academic/classes");
        using var sections = await client.GetAsync("/api/academic/sections");
        using var subjects = await client.GetAsync("/api/academic/subjects");
        Assert.Equal(HttpStatusCode.OK, classes.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sections.StatusCode);
        Assert.Equal(HttpStatusCode.OK, subjects.StatusCode);

        using var createClass = await client.PostAsJsonAsync("/api/academic/classes", new { Name = "T-Forbidden" });
        Assert.Equal(HttpStatusCode.Forbidden, createClass.StatusCode);

        using var updateClass = await client.PutAsJsonAsync("/api/academic/classes/4", new { Name = "T-Forbidden" });
        Assert.Equal(HttpStatusCode.Forbidden, updateClass.StatusCode);

        using var deleteClass = await client.DeleteAsync("/api/academic/classes/4");
        Assert.Equal(HttpStatusCode.Forbidden, deleteClass.StatusCode);

        using var createSection = await client.PostAsJsonAsync("/api/academic/sections", new { Name = "Z", ClassId = 4 });
        Assert.Equal(HttpStatusCode.Forbidden, createSection.StatusCode);

        using var deleteSection = await client.DeleteAsync("/api/academic/sections/1");
        Assert.Equal(HttpStatusCode.Forbidden, deleteSection.StatusCode);

        using var createSubject = await client.PostAsJsonAsync("/api/academic/subjects", new { Name = "T-X", Code = "TX1" });
        Assert.Equal(HttpStatusCode.Forbidden, createSubject.StatusCode);

        using var deleteSubject = await client.DeleteAsync("/api/academic/subjects/1");
        Assert.Equal(HttpStatusCode.Forbidden, deleteSubject.StatusCode);

        using var map = await client.PutAsJsonAsync("/api/academic/classes/4/subjects", new { SubjectIdList = new[] { 1 } });
        Assert.Equal(HttpStatusCode.Forbidden, map.StatusCode);
    }
}