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
/// Covers the timetable flow.
///
/// TimetableEntries was empty in every database because nothing could insert a
/// row: the only writer needed a period that no endpoint created, and the read
/// endpoint demanded a class and section the learner pages could not resolve.
/// What is worth protecting now that both sides are reachable:
///
///   1. A section sits in one subject per period per day, and a teacher teaches
///      one class at a time. Both are refused with 409, not stacked.
///   2. An entry names a real day, a section of the chosen class, and a subject
///      that class actually offers. Anything else is a 400, not a silent no-op.
///   3. The bell schedule cannot be reordered into nonsense (a period ending
///      before it starts, or overlapping another) and a period in use cannot be
///      deleted out from under its timetable.
///   4. A learner only ever reads their own week; the class is resolved
///      server-side and every other read is scoped.
/// </summary>
[Collection(AuthTestCollection.Name)]
public class TimetableTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Database=schoolhub_db;Username=postgres;Password=00000";

    private readonly AuthTestFixture _fixture;
    private readonly HttpClient _admin;
    private readonly HttpClient _teacher;
    private readonly HttpClient _student;
    private readonly HttpClient _parent;

    public TimetableTests(AuthTestFixture fixture)
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
    /// Removes every TT- throwaway row. Deleting the class cascades its sections,
    /// enrolments, class subjects and timetable entries, so classes go first;
    /// then the subjects and periods those entries referenced can go.
    /// </summary>
    private static async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync("DELETE FROM Classes WHERE Name LIKE 'TT-%'");
        await conn.ExecuteAsync("DELETE FROM Subjects WHERE Code LIKE 'TT-%'");
        await conn.ExecuteAsync("DELETE FROM TimeSlots WHERE Label LIKE 'TT-%'");
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>A throwaway class with a section and one subject it offers.</summary>
    private static async Task<(int ClassId, int SectionId, int SubjectId)> MakeClassAsync(string name)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        var classId = await conn.QuerySingleAsync<int>(
            "INSERT INTO Classes (Name) VALUES (@n) RETURNING Id", new { n = name });
        var sectionId = await conn.QuerySingleAsync<int>(
            "INSERT INTO Sections (Name, ClassId) VALUES ('A', @c) RETURNING Id", new { c = classId });

        var code = "TT-" + Guid.NewGuid().ToString("N")[..8];
        var subjectId = await conn.QuerySingleAsync<int>(
            "INSERT INTO Subjects (Name, Code) VALUES ('TT-Subject', @code) RETURNING Id",
            new { code });
        await conn.ExecuteAsync(
            "INSERT INTO ClassSubjects (ClassId, SubjectId) VALUES (@c, @s)",
            new { c = classId, s = subjectId });

        return (classId, sectionId, subjectId);
    }

    /// <summary>The Teachers.Id behind the fixture's teacher account.</summary>
    private static async Task<int> FixtureTeacherIdAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        return await conn.QuerySingleAsync<int>(@"
            SELECT t.Id FROM Teachers t
            JOIN Users u ON u.Id = t.UserId
            WHERE u.Username = @u", new { u = AuthTestFixture.TeacherUsername });
    }

    /// <summary>Moves the linked student into a throwaway class for the read tests.</summary>
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

    private async Task<int> MakePeriodAsync(string start, string end, string label = "TT-Period")
    {
        using var res = await _admin.PostAsJsonAsync("/api/schoolextensions/timeslots",
            new { StartTime = start, EndTime = end, Label = label });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("TimeSlotId").GetInt32();
    }

    private async Task<int> MakeEntryAsync(
        int classId, int sectionId, int subjectId, int periodId, int day, int teacherId = 0)
    {
        using var res = await _admin.PostAsJsonAsync("/api/schoolextensions/timetable", new
        {
            ClassId = classId,
            SectionId = sectionId,
            SubjectId = subjectId,
            TeacherId = teacherId,
            TimeSlotId = periodId,
            DayOfWeek = day,
        });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("Id").GetInt32();
    }

    /* --------------------------- periods --------------------------- */

    [Fact]
    public async Task Period_Rejects_Bad_Times_And_Overlap_And_Lists_What_It_Creates()
    {
        await ResetAsync();

        using (var backwards = await _admin.PostAsJsonAsync("/api/schoolextensions/timeslots",
            new { StartTime = "13:00", EndTime = "13:00", Label = "TT-Bad" }))
            Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);

        using (var malformed = await _admin.PostAsJsonAsync("/api/schoolextensions/timeslots",
            new { StartTime = "lunch", EndTime = "13:00", Label = "TT-Bad" }))
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

        await MakePeriodAsync("13:00", "13:45");

        // 13:30 falls inside the first period, so it is refused rather than
        // creating two blocks a teacher could be double-booked across.
        using (var overlap = await _admin.PostAsJsonAsync("/api/schoolextensions/timeslots",
            new { StartTime = "13:30", EndTime = "14:15", Label = "TT-Bad" }))
            Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);

        await MakePeriodAsync("14:00", "14:45");

        using var list = await _student.GetAsync("/api/schoolextensions/timeslots");
        list.EnsureSuccessStatusCode();
        var created = (await ReadJson(list)).EnumerateArray()
            .Where(p => p.GetProperty("StartTime").GetString()!.StartsWith("13:") ||
                        p.GetProperty("StartTime").GetString()!.StartsWith("14:"))
            .ToList();
        Assert.Equal(2, created.Count);
    }

    [Fact]
    public async Task Period_Can_Be_Updated_And_Deleted_While_Unused()
    {
        await ResetAsync();
        var periodId = await MakePeriodAsync("13:00", "13:45");

        using (var update = await _admin.PutAsJsonAsync($"/api/schoolextensions/timeslots/{periodId}",
            new { StartTime = "13:05", EndTime = "13:50", Label = "TT-Moved" }))
            update.EnsureSuccessStatusCode();

        using (var delete = await _admin.DeleteAsync($"/api/schoolextensions/timeslots/{periodId}"))
            delete.EnsureSuccessStatusCode();

        using var gone = await _admin.DeleteAsync($"/api/schoolextensions/timeslots/{periodId}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Period_In_Use_By_The_Timetable_Cannot_Be_Deleted()
    {
        await ResetAsync();
        var (classId, sectionId, subjectId) = await MakeClassAsync("TT-A");
        var periodId = await MakePeriodAsync("13:00", "13:45");
        await MakeEntryAsync(classId, sectionId, subjectId, periodId, 1);

        // The FK cascades, so allowing this would silently wipe the entry.
        using var delete = await _admin.DeleteAsync($"/api/schoolextensions/timeslots/{periodId}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    /* --------------------------- entry validation --------------------------- */

    [Fact]
    public async Task Entry_Rejects_A_Bad_Day_Wrong_Section_Unoffered_Subject_And_Unknown_Period()
    {
        await ResetAsync();
        var (classId, sectionId, subjectId) = await MakeClassAsync("TT-A");
        var (_, otherSection, otherSubject) = await MakeClassAsync("TT-B");
        var periodId = await MakePeriodAsync("13:00", "13:45");

        async Task<HttpStatusCode> PostAsync(int clazz, int section, int subject, int day, int period)
        {
            using var res = await _admin.PostAsJsonAsync("/api/schoolextensions/timetable", new
            {
                ClassId = clazz,
                SectionId = section,
                SubjectId = subject,
                TeacherId = 0,
                TimeSlotId = period,
                DayOfWeek = day,
            });
            return res.StatusCode;
        }

        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(classId, sectionId, subjectId, 0, periodId));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(classId, sectionId, subjectId, 8, periodId));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(classId, otherSection, subjectId, 1, periodId));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(classId, sectionId, otherSubject, 1, periodId));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(classId, sectionId, subjectId, 1, 999_999));
    }

    /* --------------------------- clashes --------------------------- */

    [Fact]
    public async Task Section_And_Teacher_Clashes_Are_Refused()
    {
        await ResetAsync();
        var (classId, sectionId, subjectId) = await MakeClassAsync("TT-A");
        var (class2, section2, subject2) = await MakeClassAsync("TT-B");
        var teacherId = await FixtureTeacherIdAsync();
        var period1 = await MakePeriodAsync("13:00", "13:45");
        var period2 = await MakePeriodAsync("14:00", "14:45");

        await MakeEntryAsync(classId, sectionId, subjectId, period1, 1, teacherId);

        // Same section, same period, same day: the second would render stacked.
        using (var sectionClash = await _admin.PostAsJsonAsync("/api/schoolextensions/timetable", new
        {
            ClassId = classId,
            SectionId = sectionId,
            SubjectId = subjectId,
            TeacherId = 0,
            TimeSlotId = period1,
            DayOfWeek = 1,
        }))
            Assert.Equal(HttpStatusCode.Conflict, sectionClash.StatusCode);

        // Same teacher, another class, same period and day: impossible to teach.
        using (var teacherClash = await _admin.PostAsJsonAsync("/api/schoolextensions/timetable", new
        {
            ClassId = class2,
            SectionId = section2,
            SubjectId = subject2,
            TeacherId = teacherId,
            TimeSlotId = period1,
            DayOfWeek = 1,
        }))
            Assert.Equal(HttpStatusCode.Conflict, teacherClash.StatusCode);

        // The same teacher in the next period is fine.
        await MakeEntryAsync(class2, section2, subject2, period2, 1, teacherId);
    }

    [Fact]
    public async Task Update_An_Unchanged_Entry_Does_Not_Clash_With_Itself()
    {
        await ResetAsync();
        var (classId, sectionId, subjectId) = await MakeClassAsync("TT-A");
        var periodId = await MakePeriodAsync("13:00", "13:45");
        var entryId = await MakeEntryAsync(classId, sectionId, subjectId, periodId, 2);

        using (var update = await _admin.PutAsJsonAsync($"/api/schoolextensions/timetable/{entryId}", new
        {
            ClassId = classId,
            SectionId = sectionId,
            SubjectId = subjectId,
            TeacherId = 0,
            TimeSlotId = periodId,
            DayOfWeek = 2,
        }))
            update.EnsureSuccessStatusCode();

        using (var delete = await _admin.DeleteAsync($"/api/schoolextensions/timetable/{entryId}"))
            delete.EnsureSuccessStatusCode();

        using var gone = await _admin.DeleteAsync($"/api/schoolextensions/timetable/{entryId}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    /* --------------------------- reads --------------------------- */

    [Fact]
    public async Task Read_Requires_A_Matching_Class_And_Section_And_Orders_The_Week()
    {
        await ResetAsync();
        var (classId, sectionId, subjectId) = await MakeClassAsync("TT-A");
        var (_, otherSection, _) = await MakeClassAsync("TT-B");
        var friday = await MakePeriodAsync("13:00", "13:45");
        var monday = await MakePeriodAsync("14:00", "14:45");

        await MakeEntryAsync(classId, sectionId, subjectId, friday, 5);
        await MakeEntryAsync(classId, sectionId, subjectId, monday, 1);

        using (var noParams = await _admin.GetAsync("/api/schoolextensions/timetable"))
            Assert.Equal(HttpStatusCode.BadRequest, noParams.StatusCode);

        using (var mismatched = await _admin.GetAsync(
            $"/api/schoolextensions/timetable?classId={classId}&sectionId={otherSection}"))
            Assert.Equal(HttpStatusCode.BadRequest, mismatched.StatusCode);

        using var ok = await _admin.GetAsync(
            $"/api/schoolextensions/timetable?classId={classId}&sectionId={sectionId}");
        ok.EnsureSuccessStatusCode();
        var days = (await ReadJson(ok)).EnumerateArray()
            .Select(r => r.GetProperty("DayOfWeek").GetInt32())
            .ToList();
        Assert.Equal(new[] { 1, 5 }, days);
    }

    [Fact]
    public async Task Student_Sees_Their_Own_Class_Timetable()
    {
        await ResetAsync();
        var (classId, sectionId, subjectId) = await MakeClassAsync("TT-A");
        var periodId = await MakePeriodAsync("13:00", "13:45");
        await MakeEntryAsync(classId, sectionId, subjectId, periodId, 2);
        await EnrollLinkedStudentAsync(classId, sectionId);

        using var mine = await _student.GetAsync("/api/schoolextensions/timetable/mine");
        mine.EnsureSuccessStatusCode();
        var rows = (await ReadJson(mine)).EnumerateArray().ToList();
        var row = Assert.Single(rows);
        Assert.Equal(2, row.GetProperty("DayOfWeek").GetInt32());
        Assert.Equal("TT-Subject", row.GetProperty("SubjectName").GetString());
    }

    [Fact]
    public async Task A_Learner_Can_Only_Read_Their_Own_Student_Timetable()
    {
        await ResetAsync();

        // Student: own record, never a sibling's.
        using (var own = await _student.GetAsync(
            $"/api/schoolextensions/timetable/student/{_fixture.LinkedStudentId}"))
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using (var other = await _student.GetAsync(
            $"/api/schoolextensions/timetable/student/{_fixture.UnlinkedStudentId}"))
            Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);

        // Parent: a linked child, never an unlinked one.
        using (var linked = await _parent.GetAsync(
            $"/api/schoolextensions/timetable/student/{_fixture.LinkedStudentId}"))
            Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        using (var unlinked = await _parent.GetAsync(
            $"/api/schoolextensions/timetable/student/{_fixture.UnlinkedStudentId}"))
            Assert.Equal(HttpStatusCode.Forbidden, unlinked.StatusCode);

        // Staff: any student.
        using (var staff = await _teacher.GetAsync(
            $"/api/schoolextensions/timetable/student/{_fixture.UnlinkedStudentId}"))
            Assert.Equal(HttpStatusCode.OK, staff.StatusCode);
    }

    /* --------------------------- authorization --------------------------- */

    [Fact]
    public async Task Timetable_Writes_Are_Admin_Only()
    {
        await ResetAsync();

        using (var teacherPeriod = await _teacher.PostAsJsonAsync("/api/schoolextensions/timeslots",
            new { StartTime = "13:00", EndTime = "13:45", Label = "TT-Nope" }))
            Assert.Equal(HttpStatusCode.Forbidden, teacherPeriod.StatusCode);

        using (var studentEntry = await _student.PostAsJsonAsync("/api/schoolextensions/timetable",
            new { ClassId = 1, SectionId = 1, SubjectId = 1, TeacherId = 0, TimeSlotId = 1, DayOfWeek = 1 }))
            Assert.Equal(HttpStatusCode.Forbidden, studentEntry.StatusCode);

        using (var teacherUpdate = await _teacher.PutAsJsonAsync("/api/schoolextensions/timetable/1",
            new { ClassId = 1, SectionId = 1, SubjectId = 1, TeacherId = 0, TimeSlotId = 1, DayOfWeek = 1 }))
            Assert.Equal(HttpStatusCode.Forbidden, teacherUpdate.StatusCode);

        using (var studentDelete = await _student.DeleteAsync("/api/schoolextensions/timetable/1"))
            Assert.Equal(HttpStatusCode.Forbidden, studentDelete.StatusCode);
    }
}
