using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
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
/// Covers the Admin write endpoints behind the student management UI, plus the
/// two bugs the UI work uncovered:
///
///   * UpdateStudent and AssignStudentToClass returned 200 for a student that
///     does not exist, so the page reported success for a no-op.
///   * GET /api/students joined every enrolment row, rendering four students as
///     eighteen, because Enrollments had no uniqueness on StudentId.
///
/// Every test cleans up after itself; the fixture removes the created user and
/// the cascade takes the student and its enrolment with it.
/// </summary>
[Collection(AuthTestCollection.Name)]
public class StudentManagementTests
{
    private const string ConnectionString =
        "Host=localhost;Database=schoolhub_db;Username=postgres;Password=00000";

    private readonly AuthTestFixture _fixture;

    public StudentManagementTests(AuthTestFixture fixture) => _fixture = fixture;

    private HttpClient Admin => AuthTestFixture.ClientFor(_fixture, _fixture.AdminToken);
    private HttpClient Teacher => AuthTestFixture.ClientFor(_fixture, _fixture.TeacherToken);

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<int> CountEnrollmentsAsync(int studentId)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM enrollments WHERE studentid = @id", new { id = studentId });
    }

    private async Task<(int StudentId, int UserId)> CreateStudentAsync(string rollNumber)
    {
        using var client = Admin;

        HttpResponseMessage? response = null;
        try
        {
            response = await client.PostAsJsonAsync("/api/students", new
            {
                Username = AuthTestFixture.WritableStudentUsername,
                Email = "w_student@test.local",
                Password = "Writable@Pass123",
                RollNumber = rollNumber,
                AdmissionDate = "2026-01-15",
            });
        }
        finally
        {
            // A leftover row from an interrupted run would collide on the
            // username, so retry once against a clean slate.
            if (response?.StatusCode == HttpStatusCode.Conflict)
            {
                response.Dispose();
                await ResetWritableStudentAsync();
                response = await client.PostAsJsonAsync("/api/students", new
                {
                    Username = AuthTestFixture.WritableStudentUsername,
                    Email = "w_student@test.local",
                    Password = "Writable@Pass123",
                    RollNumber = rollNumber,
                });
            }
        }

        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
            var body = await ReadJson(response);
            return (body.GetProperty("StudentId").GetInt32(), body.GetProperty("UserId").GetInt32());
        }
    }

    private static async Task ResetWritableStudentAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "DELETE FROM users WHERE username = @u",
            new { u = AuthTestFixture.WritableStudentUsername });
    }

    private async Task<(int ClassId, int SectionId)> FirstClassAndSectionAsync()
    {
        using var client = Admin;

        using var classes = await client.GetAsync("/api/academic/classes");
        var classList = (await ReadJson(classes)).EnumerateArray().ToList();
        Assert.NotEmpty(classList);

        using var sections = await client.GetAsync("/api/academic/sections");
        var sectionList = (await ReadJson(sections)).EnumerateArray().ToList();
        Assert.NotEmpty(sectionList);

        var classId = classList[0].GetProperty("Id").GetInt32();
        // Sections now return ClassId; without it the UI had to match by name.
        var section = sectionList.First(s => s.GetProperty("ClassId").GetInt32() == classId);
        return (classId, section.GetProperty("Id").GetInt32());
    }

    /* ------------------------- create ------------------------- */

    [Fact]
    public async Task Admin_Can_Create_A_Student()
    {
        await ResetWritableStudentAsync();
        var (studentId, userId) = await CreateStudentAsync("W-001");

        Assert.True(studentId > 0);
        Assert.True(userId > 0);
    }

    [Fact]
    public async Task Created_Student_Gets_The_Student_Role_On_Both_Tables()
    {
        await ResetWritableStudentAsync();
        var (_, userId) = await CreateStudentAsync("W-002");

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        // Login reads Users.Role, not the UserRoles join, so the column has to be
        // set or the new account authenticates with the wrong role.
        var columnRole = await conn.ExecuteScalarAsync<string>(
            "SELECT role FROM users WHERE id = @id", new { id = userId });
        Assert.Equal("Student", columnRole);

        var roleNames = (await conn.QueryAsync<string>(
            """
            SELECT r.name FROM userroles ur
            JOIN roles r ON r.id = ur.roleid
            WHERE ur.userid = @id
            """, new { id = userId })).ToList();
        Assert.Contains("Student", roleNames);
    }

    [Fact]
    public async Task Duplicate_Roll_Number_Returns_409_Not_500()
    {
        await ResetWritableStudentAsync();
        await CreateStudentAsync("W-003");

        using var client = Admin;
        using var response = await client.PostAsJsonAsync("/api/students", new
        {
            Username = "w_student_dup",
            Email = "w_student_dup@test.local",
            Password = "Writable@Pass123",
            RollNumber = "W-003",
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await ResetWritableStudentAsync();
    }

    /* ------------------------- update ------------------------- */

    [Fact]
    public async Task Admin_Can_Change_Roll_Number()
    {
        await ResetWritableStudentAsync();
        var (studentId, _) = await CreateStudentAsync("W-004");

        using var client = Admin;
        using var response = await client.PutAsJsonAsync(
            $"/api/students/{studentId}", new { RollNumber = "W-004-B" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var check = await client.GetAsync($"/api/students/{studentId}");
        var body = await ReadJson(check);
        Assert.Equal("W-004-B", body.GetProperty("RollNumber").GetString());
    }

    [Fact]
    public async Task Updating_An_Unknown_Student_Returns_404()
    {
        // The UPDATE matched nothing and still returned 200, so the UI claimed
        // success for a student that does not exist.
        using var client = Admin;
        using var response = await client.PutAsJsonAsync(
            "/api/students/999999", new { RollNumber = "NOPE" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Blank_Roll_Number_Returns_400()
    {
        await ResetWritableStudentAsync();
        var (studentId, _) = await CreateStudentAsync("W-005");

        using var client = Admin;
        using var response = await client.PutAsJsonAsync(
            $"/api/students/{studentId}", new { RollNumber = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /* ------------------ deactivate / reactivate ------------------ */

    [Fact]
    public async Task Deactivate_Then_Reactivate_Toggles_IsActive()
    {
        await ResetWritableStudentAsync();
        var (studentId, _) = await CreateStudentAsync("W-006");
        using var client = Admin;

        using (var off = await client.PatchAsync($"/api/students/{studentId}/deactivate", null))
            Assert.Equal(HttpStatusCode.OK, off.StatusCode);

        using (var check = await client.GetAsync($"/api/students/{studentId}"))
        {
            var body = await ReadJson(check);
            Assert.False(body.GetProperty("IsActive").GetBoolean());
        }

        using (var on = await client.PatchAsync($"/api/students/{studentId}/reactivate", null))
            Assert.Equal(HttpStatusCode.OK, on.StatusCode);

        using (var check = await client.GetAsync($"/api/students/{studentId}"))
        {
            var body = await ReadJson(check);
            Assert.True(body.GetProperty("IsActive").GetBoolean());
        }
    }

    [Fact]
    public async Task Deactivating_An_Unknown_Student_Returns_404()
    {
        using var client = Admin;
        using var response = await client.PatchAsync("/api/students/999999/deactivate", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /* --------------------- class assignment --------------------- */

    [Fact]
    public async Task Assigning_A_Class_Repeatedly_Keeps_Exactly_One_Enrolment()
    {
        // The regression from the seed data: no unique index on Enrollments, and
        // the old SELECT-then-UPDATE wrote every row for the student.
        await ResetWritableStudentAsync();
        var (studentId, _) = await CreateStudentAsync("W-007");
        var (classId, sectionId) = await FirstClassAndSectionAsync();

        using var client = Admin;
        for (var i = 0; i < 3; i++)
        {
            using var response = await client.PostAsJsonAsync(
                $"/api/students/{studentId}/assign-class", new { ClassId = classId, SectionId = sectionId });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(1, await CountEnrollmentsAsync(studentId));
    }

    [Fact]
    public async Task Reassigning_A_Class_Replaces_Rather_Than_Appends()
    {
        await ResetWritableStudentAsync();
        var (studentId, _) = await CreateStudentAsync("W-008");

        using var client = Admin;
        using var sectionsResponse = await client.GetAsync("/api/academic/sections");
        var sections = (await ReadJson(sectionsResponse)).EnumerateArray().ToList();

        // Pick a class that genuinely has two sections rather than assuming the
        // first one does. Taking sections[0] made this depend on whatever rows
        // another test class happened to leave behind, so it passed alone and
        // failed in a full run.
        var sectionIds = sections
            .GroupBy(s => s.GetProperty("ClassId").GetInt32())
            .FirstOrDefault(g => g.Count() >= 2)?
            .Select(s => s.GetProperty("Id").GetInt32())
            .ToList()
            ?? new List<int>();
        Assert.True(sectionIds.Count >= 2, "fixture needs a class with two sections");
        var classId = sections
            .First(s => s.GetProperty("Id").GetInt32() == sectionIds[0])
            .GetProperty("ClassId")
            .GetInt32();

        using (var first = await client.PostAsJsonAsync(
            $"/api/students/{studentId}/assign-class",
            new { ClassId = classId, SectionId = sectionIds[0] }))
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using (var second = await client.PostAsJsonAsync(
            $"/api/students/{studentId}/assign-class",
            new { ClassId = classId, SectionId = sectionIds[1] }))
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        Assert.Equal(1, await CountEnrollmentsAsync(studentId));

        // The roster must now report the second section, not the first.
        using var check = await client.GetAsync($"/api/students/{studentId}");
        var body = await ReadJson(check);
        var expectedName = sections
            .First(s => s.GetProperty("Id").GetInt32() == sectionIds[1])
            .GetProperty("Name").GetString();
        Assert.Equal(expectedName, body.GetProperty("SectionName").GetString());
    }

    [Fact]
    public async Task Assigning_To_An_Unknown_Student_Returns_404()
    {
        using var client = Admin;
        using var response = await client.PostAsJsonAsync(
            "/api/students/999999/assign-class", new { ClassId = 1, SectionId = 1 });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Assigning_With_Zero_Ids_Returns_400()
    {
        await ResetWritableStudentAsync();
        var (studentId, _) = await CreateStudentAsync("W-009");

        using var client = Admin;
        using var response = await client.PostAsJsonAsync(
            $"/api/students/{studentId}/assign-class", new { ClassId = 0, SectionId = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /* ------------------------- roster shape ------------------------- */

    [Fact]
    public async Task Roster_Returns_Each_Student_Exactly_Once()
    {
        // Four students rendered as eighteen rows because the query joined every
        // enrolment instead of a single one.
        using var client = Admin;
        using var response = await client.GetAsync("/api/students");
        response.EnsureSuccessStatusCode();

        var rows = (await ReadJson(response)).EnumerateArray().ToList();
        var ids = rows.Select(r => r.GetProperty("Id").GetInt32()).ToList();

        Assert.NotEmpty(rows);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task Search_Also_Returns_Each_Student_Once()
    {
        using var client = Admin;
        using var response = await client.GetAsync("/api/students/search?query=");
        response.EnsureSuccessStatusCode();

        var rows = (await ReadJson(response)).EnumerateArray().ToList();
        var ids = rows.Select(r => r.GetProperty("Id").GetInt32()).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task Sections_Expose_ClassId()
    {
        using var client = Admin;
        using var response = await client.GetAsync("/api/academic/sections");
        response.EnsureSuccessStatusCode();

        var sections = (await ReadJson(response)).EnumerateArray().ToList();
        Assert.NotEmpty(sections);
        foreach (var section in sections)
        {
            // The UI cannot filter sections by class without this; it used to
            // match on ClassName, which breaks when two classes share a name.
            Assert.Equal(JsonValueKind.Number, section.GetProperty("ClassId").ValueKind);
        }
    }

    /* ------------------------- role boundary ------------------------- */

    [Fact]
    public async Task Teacher_May_Read_But_May_Not_Write_Students()
    {
        await ResetWritableStudentAsync();
        var (studentId, _) = await CreateStudentAsync("W-010");

        using var client = Teacher;

        using var roster = await client.GetAsync("/api/students");
        Assert.Equal(HttpStatusCode.OK, roster.StatusCode);

        using var create = await client.PostAsJsonAsync("/api/students", new
        {
            Username = "w_teacher_attempt",
            Email = "w_teacher_attempt@test.local",
            Password = "Writable@Pass123",
            RollNumber = "W-010-B",
        });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        using var update = await client.PutAsJsonAsync(
            $"/api/students/{studentId}", new { RollNumber = "HACK" });
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);

        using var deactivate = await client.PatchAsync($"/api/students/{studentId}/deactivate", null);
        Assert.Equal(HttpStatusCode.Forbidden, deactivate.StatusCode);

        using var reactivate = await client.PatchAsync($"/api/students/{studentId}/reactivate", null);
        Assert.Equal(HttpStatusCode.Forbidden, reactivate.StatusCode);

        using var assign = await client.PostAsJsonAsync(
            $"/api/students/{studentId}/assign-class", new { ClassId = 1, SectionId = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, assign.StatusCode);
    }
}
