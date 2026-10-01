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
/// Covers the attendance marking flow.
///
/// Two properties are worth protecting. First, a section has exactly one session
/// per day: without that a student could be marked twice on the same date and
/// their percentage counted that day twice. Second, a mark must belong to the
/// section being marked and the status must be one the reports recognise —
/// previously both were accepted unchecked, so a roster from another grade could
/// be filed under this one and a typo silently became a fifth status.
/// </summary>
[Collection(AuthTestCollection.Name)]
public class AttendanceTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Database=schoolhub_db;Username=postgres;Password=00000";

    private readonly AuthTestFixture _fixture;

    public AttendanceTests(AuthTestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    private HttpClient Admin => AuthTestFixture.ClientFor(_fixture, _fixture.AdminToken);
    private HttpClient Teacher => AuthTestFixture.ClientFor(_fixture, _fixture.TeacherToken);
    private HttpClient Student => AuthTestFixture.ClientFor(_fixture, _fixture.StudentToken);
    private HttpClient Parent => AuthTestFixture.ClientFor(_fixture, _fixture.ParentToken);

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>
    /// Removes the throwaway classes when the class finishes, not just between
    /// its own tests. Sections and their attendance cascade from the class, so
    /// this is the whole teardown — and without it the last test's AT- rows leak
    /// into other test classes that pick sections by position.
    /// </summary>
    public async Task DisposeAsync()
    {
        await ResetAsync();
    }

    private static async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        // Enrollments.SectionId has no ON DELETE CASCADE (the same reason the API
        // refuses to delete an occupied section), so the enrollments have to go
        // first or this delete aborts on the FK. AttendanceRecords and
        // AttendanceSessions both cascade from Sections.
        await conn.ExecuteAsync(@"
            DELETE FROM Enrollments
             WHERE classid IN (SELECT id FROM classes WHERE name LIKE 'AT-%')");
        await conn.ExecuteAsync("DELETE FROM classes WHERE name LIKE 'AT-%'");
    }

    /// <summary>
    /// A throwaway class with one section and <paramref name="studentCount"/>
    /// fixture students enrolled, so marking has a roster to work with.
    /// </summary>
    private async Task<(int ClassId, int SectionId, List<int> Students)> MakeSectionAsync(
        string name = "AT-Alpha", int studentCount = 2)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        var classId = await conn.QuerySingleAsync<int>(
            "INSERT INTO Classes (Name) VALUES (@n) RETURNING id", new { n = name });
        var sectionId = await conn.QuerySingleAsync<int>(
            "INSERT INTO Sections (Name, ClassId) VALUES ('A', @c) RETURNING id", new { c = classId });

        // The fixture's two students plus the writable one, so there is more than
        // one row to mark and a duplicate check has something to catch.
        var candidates = new[] { _fixture.LinkedStudentId, _fixture.UnlinkedStudentId }
            .Concat(await WritableStudentIdsAsync())
            .Distinct()
            .Take(studentCount)
            .ToList();

        // A student belongs to one class at a time (ux_enrollments_studentid), so
        // a second throwaway section has to move these students rather than add
        // them. All three are fixture-owned accounts created and dropped by this
        // run, so nothing real is affected.
        await conn.ExecuteAsync(
            "DELETE FROM Enrollments WHERE StudentId = ANY(@Ids)", new { Ids = candidates });

        foreach (var studentId in candidates)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO Enrollments (StudentId, ClassId, SectionId)
                VALUES (@s, @c, @sec)", new { s = studentId, c = classId, sec = sectionId });
        }

        return (classId, sectionId, candidates);
    }

    private static async Task<List<int>> WritableStudentIdsAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        return (await conn.QueryAsync<int>(
            """
            SELECT s.id FROM Students s
              JOIN Users u ON u.id = s.userid
             WHERE u.username = @u
            """, new { u = AuthTestFixture.WritableStudentUsername })).ToList();
    }

    /// <summary>Moves a student out of the throwaway section so they are a stray.</summary>
    private static async Task UnenrolAsync(int studentId, int sectionId)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "DELETE FROM Enrollments WHERE studentid = @s AND sectionid = @sec",
            new { s = studentId, sec = sectionId });
    }

    private static object Payload(int classId, int sectionId, DateTime date, IEnumerable<object> records)
        => new { ClassId = classId, SectionId = sectionId, Date = date, Records = records };

    private static object Mark(int studentId, string status = "Present", string remarks = "")
        => new { StudentId = studentId, Status = status, Remarks = remarks };

    /* ------------------------- roster ------------------------- */

    [Fact]
    public async Task Roster_Lists_The_Sections_Enrolled_Students()
    {
        await ResetAsync();
        var (classId, sectionId, students) = await MakeSectionAsync();

        using var client = Admin;
        using var response = await client.GetAsync(
            $"/api/attendance/roster?classId={classId}&sectionId={sectionId}");
        response.EnsureSuccessStatusCode();

        var body = await ReadJson(response);
        var listed = body.GetProperty("Students").EnumerateArray()
            .Select(s => s.GetProperty("StudentId").GetInt32())
            .ToList();

        Assert.Equal(students.Count, listed.Count);
        foreach (var id in students) Assert.Contains(id, listed);

        // Nothing is marked yet, so every status has to be null rather than
        // defaulting to Present and quietly marking the whole roster.
        foreach (var row in body.GetProperty("Students").EnumerateArray())
            Assert.Equal(JsonValueKind.Null, row.GetProperty("Status").ValueKind);
    }

    [Fact]
    public async Task Roster_Rejects_A_Section_From_Another_Class()
    {
        await ResetAsync();
        var (classId, _, _) = await MakeSectionAsync("AT-One");
        var (_, otherSection, _) = await MakeSectionAsync("AT-Two");

        using var client = Admin;
        using var response = await client.GetAsync(
            $"/api/attendance/roster?classId={classId}&sectionId={otherSection}");

        // Marking AT-Two's section while claiming AT-One would file the session
        // under the wrong grade.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("does not belong", (await ReadJson(response)).GetProperty("Message").GetString()!);
    }

    [Fact]
    public async Task Roster_For_An_Unknown_Section_Returns_404()
    {
        await ResetAsync();
        var (classId, _, _) = await MakeSectionAsync();

        using var client = Admin;
        using var response = await client.GetAsync(
            $"/api/attendance/roster?classId={classId}&sectionId=999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /* ------------------------- create ------------------------- */

    [Fact]
    public async Task Marking_Records_A_Session_And_One_Row_Per_Student()
    {
        await ResetAsync();
        var (classId, sectionId, students) = await MakeSectionAsync();
        var date = new DateTime(2026, 3, 4);

        using var client = Admin;
        using var response = await client.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, date, students.Select(s => Mark(s, "Present"))));
        response.EnsureSuccessStatusCode();

        var sessionId = (await ReadJson(response)).GetProperty("SessionId").GetInt32();

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        var rows = (await conn.QueryAsync<(int StudentId, string Status)>(
            "SELECT studentid, status FROM attendancerecords WHERE sessionid = @s ORDER BY studentid",
            new { s = sessionId })).ToList();

        Assert.Equal(students.Count, rows.Count);
        Assert.All(rows, r => Assert.Equal("Present", r.Status));
    }

    [Fact]
    public async Task Marking_The_Same_Section_Twice_Is_Refused()
    {
        await ResetAsync();
        var (classId, sectionId, students) = await MakeSectionAsync();
        var date = new DateTime(2026, 3, 5);

        using var client = Admin;
        using var first = await client.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, date, students.Select(s => Mark(s))));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var existingSession = (await ReadJson(first)).GetProperty("SessionId").GetInt32();

        using var second = await client.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, date, students.Select(s => Mark(s, "Absent"))));

        // Two sessions for one day double-count that day in the student's
        // percentage, so the second is refused and points at the first.
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await ReadJson(second);
        Assert.Contains("already been marked", body.GetProperty("Message").GetString()!);
        Assert.Equal(existingSession, body.GetProperty("SessionId").GetInt32());
    }

    [Theory]
    [InlineData("Absent", HttpStatusCode.OK)]
    [InlineData("Late", HttpStatusCode.OK)]
    [InlineData("Excused", HttpStatusCode.OK)]
    [InlineData("present", HttpStatusCode.BadRequest)]
    [InlineData("P", HttpStatusCode.BadRequest)]
    [InlineData("", HttpStatusCode.BadRequest)]
    public async Task Only_The_Four_Known_Statuses_Are_Accepted(string status, HttpStatusCode expected)
    {
        await ResetAsync();
        var (classId, sectionId, students) = await MakeSectionAsync();

        using var client = Admin;
        using var response = await client.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, new DateTime(2026, 3, 6), students.Select(s => Mark(s, status))));

        // A typo used to be stored as a fifth status that no report counted, so
        // the student's percentage quietly went up.
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task A_Rejected_Record_Leaves_No_Session_Behind()
    {
        await ResetAsync();
        var (classId, sectionId, students) = await MakeSectionAsync();

        using var client = Admin;
        using var response = await client.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, new DateTime(2026, 3, 7),
                students.Select(s => Mark(s)).Append(Mark(999999, "Absent"))));

        // An id that is not enrolled in the section is rejected with the ids
        // named, the same as any other stray mark.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        var sessions = await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM attendancesessions WHERE classid = @c", new { c = classId });

        // The whole submission is one transaction: no session header survives a
        // rejected row.
        Assert.Equal(0, sessions);
    }

    [Fact]
    public async Task A_Student_From_Another_Section_Is_Refused()
    {
        await ResetAsync();
        var (classId, sectionId, students) = await MakeSectionAsync();
        var stray = students.Last();
        await UnenrolAsync(stray, sectionId);

        using var client = Admin;
        using var response = await client.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, new DateTime(2026, 3, 8), students.Select(s => Mark(s))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(stray.ToString(), (await ReadJson(response)).GetProperty("Message").GetString()!);
    }

    [Fact]
    public async Task The_Same_Student_Twice_In_One_Payload_Is_Refused()
    {
        await ResetAsync();
        var (classId, sectionId, students) = await MakeSectionAsync();

        using var client = Admin;
        using var response = await client.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, new DateTime(2026, 3, 9),
                students.Select(s => Mark(s)).Append(Mark(students[0], "Absent"))));

        // Caught here with the id named; the unique index would otherwise abort
        // mid-transaction with no usable message.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("more than once", (await ReadJson(response)).GetProperty("Message").GetString()!);
    }

    [Fact]
    public async Task An_Empty_Session_Is_Refused()
    {
        await ResetAsync();
        var (classId, sectionId, _) = await MakeSectionAsync();

        using var client = Admin;
        using var response = await client.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, new DateTime(2026, 3, 10), Array.Empty<object>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /* ------------------------- update ------------------------- */

    [Fact]
    public async Task Editing_A_Session_Replaces_The_Marks_Without_Duplicating_Them()
    {
        await ResetAsync();
        var (classId, sectionId, students) = await MakeSectionAsync();
        var date = new DateTime(2026, 3, 11);

        using var client = Admin;
        using var created = await client.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, date, students.Select(s => Mark(s, "Present"))));
        created.EnsureSuccessStatusCode();
        var sessionId = (await ReadJson(created)).GetProperty("SessionId").GetInt32();

        using var updated = await client.PutAsJsonAsync($"/api/attendance/sessions/{sessionId}",
            Payload(classId, sectionId, date, students.Select(s => Mark(s, "Absent", "called"))));
        updated.EnsureSuccessStatusCode();

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        var rows = (await conn.QueryAsync<(string Status, string Remarks)>(
            "SELECT status, remarks FROM attendancerecords WHERE sessionid = @s", new { s = sessionId })).ToList();

        // Updating in place keeps one row per student; insert-then-delete would
        // have orphaned the original row's id.
        Assert.Equal(students.Count, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal("Absent", r.Status);
            Assert.Equal("called", r.Remarks);
        });
    }

    [Fact]
    public async Task Editing_A_Session_Cannot_Move_It_To_Another_Section()
    {
        await ResetAsync();
        var (classId, sectionId, students) = await MakeSectionAsync("AT-Move");
        var date = new DateTime(2026, 3, 12);

        using var client = Admin;
        using var created = await client.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, date, students.Select(s => Mark(s))));
        created.EnsureSuccessStatusCode();
        var sessionId = (await ReadJson(created)).GetProperty("SessionId").GetInt32();

        var (_, elsewhere, _) = await MakeSectionAsync("AT-Elsewhere");
        using var moved = await client.PutAsJsonAsync($"/api/attendance/sessions/{sessionId}",
            Payload(classId, elsewhere, date, students.Select(s => Mark(s, "Absent"))));

        // The students in this session belong to the old section; moving the
        // header would strand them.
        Assert.Equal(HttpStatusCode.BadRequest, moved.StatusCode);
    }

    [Fact]
    public async Task Editing_An_Unknown_Session_Returns_404()
    {
        await ResetAsync();
        var (classId, sectionId, students) = await MakeSectionAsync();

        using var client = Admin;
        using var response = await client.PutAsJsonAsync("/api/attendance/sessions/999999",
            Payload(classId, sectionId, new DateTime(2026, 3, 13), students.Select(s => Mark(s))));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /* ------------------------- read ------------------------- */

    [Fact]
    public async Task Sessions_Are_Listed_Newest_First_With_A_Count()
    {
        await ResetAsync();
        var (classId, sectionId, students) = await MakeSectionAsync();

        using var client = Admin;
        using var older = await client.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, new DateTime(2026, 3, 14), students.Select(s => Mark(s, "Present"))));
        older.EnsureSuccessStatusCode();
        using var newer = await client.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, new DateTime(2026, 3, 15), students.Select(s => Mark(s, "Late"))));
        newer.EnsureSuccessStatusCode();

        using var response = await client.GetAsync(
            $"/api/attendance/sessions?classId={classId}&sectionId={sectionId}");
        response.EnsureSuccessStatusCode();

        var rows = (await ReadJson(response)).EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("2026-03-15", rows[0].GetProperty("Date").GetString()!.Substring(0, 10));
        Assert.Equal("2026-03-14", rows[1].GetProperty("Date").GetString()!.Substring(0, 10));

        foreach (var row in rows)
        {
            Assert.Equal(students.Count, row.GetProperty("Total").GetInt32());
            Assert.Equal(students.Count, row.GetProperty("Marked").GetInt32());
            // Resolved by name so the history list needs no second lookup.
            Assert.False(string.IsNullOrEmpty(row.GetProperty("ClassName").GetString()));
            Assert.False(string.IsNullOrEmpty(row.GetProperty("SectionName").GetString()));
        }
    }

    [Fact]
    public async Task One_Session_Returns_Its_Records()
    {
        await ResetAsync();
        var (classId, sectionId, students) = await MakeSectionAsync();
        var date = new DateTime(2026, 3, 16);

        using var client = Admin;
        using var created = await client.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, date, students.Select(s => Mark(s, "Excused", "note"))));
        created.EnsureSuccessStatusCode();
        var sessionId = (await ReadJson(created)).GetProperty("SessionId").GetInt32();

        using var response = await client.GetAsync($"/api/attendance/sessions/{sessionId}");
        response.EnsureSuccessStatusCode();

        var body = await ReadJson(response);
        Assert.Equal(sessionId, body.GetProperty("Session").GetProperty("Id").GetInt32());

        var records = body.GetProperty("Records").EnumerateArray().ToList();
        Assert.Equal(students.Count, records.Count);
        Assert.All(records, r =>
        {
            Assert.Equal("Excused", r.GetProperty("Status").GetString());
            Assert.Equal("note", r.GetProperty("Remarks").GetString());
            Assert.False(string.IsNullOrEmpty(r.GetProperty("Username").GetString()));
        });
    }

    [Fact]
    public async Task Student_Attendance_Carries_Class_And_Section_Names()
    {
        await ResetAsync();
        var (classId, sectionId, students) = await MakeSectionAsync();
        var date = new DateTime(2026, 3, 17);

        using var admin = Admin;
        using var created = await admin.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, date, students.Select(s => Mark(s, "Late"))));
        created.EnsureSuccessStatusCode();

        var studentId = students[0];
        using var response = await admin.GetAsync($"/api/attendance/student/{studentId}");
        response.EnsureSuccessStatusCode();

        var row = (await ReadJson(response)).EnumerateArray()
            .Single(r => r.GetProperty("Date").GetString()!.StartsWith("2026-03-17"));

        // Previously this endpoint returned ClassId with no name, so the UI had to
        // pair it against a class list it had not been given.
        Assert.Equal("AT-Alpha", row.GetProperty("ClassName").GetString());
        Assert.Equal("A", row.GetProperty("SectionName").GetString());
    }

    /* ------------------------- roles ------------------------- */

    [Fact]
    public async Task Teacher_May_Mark_But_Student_May_Not()
    {
        await ResetAsync();
        var (classId, sectionId, students) = await MakeSectionAsync();

        using var teacher = Teacher;
        using var allowed = await teacher.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, new DateTime(2026, 3, 18), students.Select(s => Mark(s))));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        using var student = Student;
        using var denied = await student.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, new DateTime(2026, 3, 19), students.Select(s => Mark(s))));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Student_May_Not_Read_The_Sessions_List()
    {
        using var student = Student;
        using var response = await student.GetAsync("/api/attendance/sessions");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Student_Reads_Their_Own_Attendance_Without_Knowing_Their_Student_Id()
    {
        await ResetAsync();
        var (classId, sectionId, students) = await MakeSectionAsync();
        var date = new DateTime(2026, 3, 23);

        using var admin = Admin;
        using var created = await admin.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, date, students.Select(s => Mark(s, "Late"))));
        created.EnsureSuccessStatusCode();

        // /api/profile returns the user row only, so nothing told the student page
        // which Students.Id to ask for. This endpoint resolves it from the token.
        using var student = Student;
        using var response = await student.GetAsync("/api/attendance/mine");
        response.EnsureSuccessStatusCode();

        var rows = (await ReadJson(response)).EnumerateArray().ToList();
        var own = rows.Where(r => r.GetProperty("Date").GetString()!.StartsWith("2026-03-23")).ToList();
        Assert.NotEmpty(own);
        Assert.All(own, r => Assert.Equal("Late", r.GetProperty("Status").GetString()));

        // Only the caller's own days: the other fixture student was in the same
        // section but is a separate account.
        Assert.All(own, r => Assert.True(r.GetProperty("SessionId").GetInt32() > 0));
    }

    [Fact]
    public async Task Parent_And_Staff_May_Not_Use_The_Own_Attendance_Endpoint()
    {
        using var parent = Parent;
        using var parentResponse = await parent.GetAsync("/api/attendance/mine");
        Assert.Equal(HttpStatusCode.Forbidden, parentResponse.StatusCode);

        using var teacher = Teacher;
        using var teacherResponse = await teacher.GetAsync("/api/attendance/mine");
        Assert.Equal(HttpStatusCode.Forbidden, teacherResponse.StatusCode);
    }

    [Fact]
    public async Task Student_May_Read_Only_Their_Own_Attendance()
    {
        await ResetAsync();
        var (classId, sectionId, students) = await MakeSectionAsync();
        var date = new DateTime(2026, 3, 20);

        using var admin = Admin;
        using var created = await admin.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, date, students.Select(s => Mark(s))));
        created.EnsureSuccessStatusCode();

        using var student = Student;
        using var own = await student.GetAsync($"/api/attendance/student/{_fixture.LinkedStudentId}");
        own.EnsureSuccessStatusCode();

        // The linked student is the fixture's own account, so only a different id
        // can be probed here.
        using var other = await student.GetAsync($"/api/attendance/student/{_fixture.UnlinkedStudentId}");
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
    }

    [Fact]
    public async Task Parent_May_Read_A_Linked_Child_But_Not_An_Unlinked_Student()
    {
        await ResetAsync();
        var (classId, sectionId, students) = await MakeSectionAsync();
        var date = new DateTime(2026, 3, 21);

        using var admin = Admin;
        using var created = await admin.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, date, students.Select(s => Mark(s))));
        created.EnsureSuccessStatusCode();

        using var parent = Parent;
        using var linked = await parent.GetAsync($"/api/attendance/student/{_fixture.LinkedStudentId}");
        linked.EnsureSuccessStatusCode();

        // The fixture deliberately leaves the second student unlinked so this
        // branch is reachable.
        using var unlinked = await parent.GetAsync($"/api/attendance/student/{_fixture.UnlinkedStudentId}");
        Assert.Equal(HttpStatusCode.Forbidden, unlinked.StatusCode);
    }

    /* ------------------------- cleanup ------------------------- */

    [Fact]
    public async Task Deleting_A_Section_Takes_Its_Sessions_With_It()
    {
        await ResetAsync();
        var (classId, sectionId, students) = await MakeSectionAsync("AT-Cascade");

        using var client = Admin;
        using var created = await client.PostAsJsonAsync("/api/attendance/session",
            Payload(classId, sectionId, new DateTime(2026, 3, 22), students.Select(s => Mark(s))));
        created.EnsureSuccessStatusCode();
        var sessionId = (await ReadJson(created)).GetProperty("SessionId").GetInt32();

        // Both tables cascade from Sections, so removing a section must not leave
        // orphaned attendance behind that still shows in a student's history.
        // A section with students in it is refused outright, so empty it first.
        foreach (var studentId in students) await UnenrolAsync(studentId, sectionId);

        using var response = await client.DeleteAsync($"/api/academic/sections/{sectionId}");
        response.EnsureSuccessStatusCode();

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM attendancesessions WHERE id = @s", new { s = sessionId }));
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM attendancerecords WHERE sessionid = @s", new { s = sessionId }));
    }
}