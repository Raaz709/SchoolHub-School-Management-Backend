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
/// Covers the examination and marks flow.
///
/// The flow was unreachable before this. Nothing in the codebase could insert an
/// ExamSubjects row, so the table was empty, and EnterMarks read its MaxMarks
/// from a row that could not exist — a teacher had no way to record a single
/// mark. Three properties are worth protecting now that it is reachable:
///
///   1. A mark has to belong to the paper's class. With no class link at all, a
///      Grade 5 mark could be filed against a Grade 11 paper.
///   2. A mark has to be inside 0..MaxMarks. Nothing checked it, and MaxMarks
///      was the divisor, so a mark above it produced a percentage above 100.
///   3. A paper's whole roster is saved or none of it is. The old handler took
///      one student per call, so a teacher could stop halfway and leave a paper
///      that looked finished.
/// </summary>
[Collection(AuthTestCollection.Name)]
public class ExamsTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Database=schoolhub_db;Username=postgres;Password=00000";

    private readonly AuthTestFixture _fixture;

    public ExamsTests(AuthTestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    private HttpClient Admin => AuthTestFixture.ClientFor(_fixture, _fixture.AdminToken);
    private HttpClient Teacher => AuthTestFixture.ClientFor(_fixture, _fixture.TeacherToken);
    private HttpClient Student => AuthTestFixture.ClientFor(_fixture, _fixture.StudentToken);
    private HttpClient Parent => AuthTestFixture.ClientFor(_fixture, _fixture.ParentToken);

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>
    /// Removes the throwaway rows when the class finishes, not just between its
    /// own tests. Without this the last test's EX- rows leak into other classes
    /// that pick exams or sections by position. Deleting the class cascades
    /// ExamSubjects, which cascades Marks, so this is the whole teardown.
    /// </summary>
    public async Task DisposeAsync() => await ResetAsync();

    private static async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        // Exams first. ExamSubjects.ClassId is ON DELETE RESTRICT, so a class
        // still holding a paper cannot be deleted at all — which is the point of
        // the guard, but it means the teardown has to unwind from the exam down:
        // the exam cascades its papers, and theirs cascade the marks.
        await conn.ExecuteAsync("DELETE FROM exams WHERE title LIKE 'EX-%'");

        // Enrollments.SectionId has no ON DELETE CASCADE (the reason the API
        // refuses to delete an occupied section), so enrollments go next.
        await conn.ExecuteAsync(@"
            DELETE FROM Enrollments
             WHERE classid IN (SELECT id FROM classes WHERE name LIKE 'EX-%')");
        await conn.ExecuteAsync("DELETE FROM classes WHERE name LIKE 'EX-%'");
        await conn.ExecuteAsync("DELETE FROM subjects WHERE code = 'EXS'");
        // Own student pool, so the count does not depend on which other test
        // classes have already run.
        await conn.ExecuteAsync("DELETE FROM users WHERE username LIKE 'ex_stu_%'");
    }

    /// <summary>
    /// Creates <paramref name="count"/> throwaway students with EX- usernames.
    ///
    /// A student belongs to one class at a time (ux_enrollments_studentid), so a
    /// second throwaway class cannot reuse the first class's roster — it would
    /// have to move them and break the first. These have to be genuinely
    /// separate accounts, which is also what makes a "student from another class"
    /// check possible at all.
    /// </summary>
    private static async Task<List<int>> MakeStudentsAsync(int count, string tag)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        var ids = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var username = $"ex_stu_{tag}_{i}";

            var userId = await conn.QuerySingleAsync<int>(@"
                INSERT INTO Users (Username, Email, PasswordHash, IsActive, Role)
                VALUES (@u, @e, 'not-a-real-hash', true, 'Student')
                RETURNING id",
                new { u = username, e = $"{username}@test.local" });

            ids.Add(await conn.QuerySingleAsync<int>(@"
                INSERT INTO Students (UserId, RollNumber)
                VALUES (@u, @r)
                RETURNING id",
                new { u = userId, r = $"EX-{tag}-{i}" }));
        }

        return ids;
    }

    /* --------------------------- fixtures --------------------------- */

    /// <summary>
    /// A throwaway class with one section and <paramref name="studentCount"/>
    /// students enrolled, so a paper sat in it has a roster to mark.
    ///
    /// The first two are the fixture's own accounts, so the /mine and parent-link
    /// paths can be exercised against a real login. Any beyond that are created
    /// fresh, because those two cannot sit in two throwaway classes at once.
    /// </summary>
    private async Task<(int ClassId, int SectionId, List<int> Students)> MakeClassAsync(
        string name = "EX-Alpha", int studentCount = 2)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        var classId = await conn.QuerySingleAsync<int>(
            "INSERT INTO Classes (Name) VALUES (@n) RETURNING id", new { n = name });
        var sectionId = await conn.QuerySingleAsync<int>(
            "INSERT INTO Sections (Name, ClassId) VALUES ('A', @c) RETURNING id", new { c = classId });

        var candidates = new List<int> { _fixture.LinkedStudentId, _fixture.UnlinkedStudentId };
        if (studentCount > candidates.Count)
        {
            candidates.AddRange(await MakeStudentsAsync(studentCount - candidates.Count, TagOf(name)));
        }
        candidates = candidates.Take(studentCount).ToList();

        // The fixture accounts may still be enrolled from an earlier class in this
        // class's own test run, and a student belongs to one class at a time.
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

    /// <summary>A stable short tag for usernames and roll numbers.</summary>
    private static string TagOf(string className) =>
        new(className.Replace("EX-", string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// The shared throwaway subject. subjects.code is unique, so this has to
    /// reuse the row rather than insert a second one.
    /// </summary>
    private static async Task<int> AnySubjectIdAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        var existing = await conn.QuerySingleOrDefaultAsync<int?>(
            "SELECT id FROM subjects WHERE code = 'EXS'");
        if (existing.HasValue) return existing.Value;

        return await conn.QuerySingleAsync<int>(
            "INSERT INTO subjects (Name, Code) VALUES ('EX-Subj', 'EXS') RETURNING id");
    }

    private static async Task<int> AnyAcademicYearIdAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        return await conn.QuerySingleAsync<int>("SELECT id FROM academicyears ORDER BY id LIMIT 1");
    }

    private static object ExamPayload(
        string title = "EX-Mid-Term",
        int? academicYearId = null,
        DateTime? start = null,
        DateTime? end = null,
        decimal? passingMarks = null)
        => new
        {
            Title = title,
            AcademicYearId = academicYearId ?? 0,
            StartDate = start,
            EndDate = end,
            PassingMarks = passingMarks,
        };

    private static object SubjectPayload(int classId, int subjectId, decimal maxMarks = 100m)
        => new { ClassId = classId, SubjectId = subjectId, MaxMarks = maxMarks };

    private static object Mark(int studentId, decimal obtained, string remarks = "")
        => new { StudentId = studentId, MarksObtained = obtained, Remarks = remarks };

    /// <summary>Creates an exam, a paper in it, and returns both ids.</summary>
    private async Task<(int ExamId, int ExamSubjectId, int ClassId, List<int> Students)> MakePaperAsync(
        string title = "EX-Mid-Term", decimal maxMarks = 100m, int studentCount = 2)
    {
        var (classId, _, students) = await MakeClassAsync(studentCount: studentCount);
        var subjectId = await AnySubjectIdAsync();
        var yearId = await AnyAcademicYearIdAsync();

        using var client = Admin;

        using var created = await client.PostAsJsonAsync("/api/exams",
            ExamPayload(title, yearId, new DateTime(2026, 4, 1), new DateTime(2026, 4, 10), 40m));
        created.EnsureSuccessStatusCode();
        var examId = (await ReadJson(created)).GetProperty("ExamId").GetInt32();

        using var paper = await client.PostAsJsonAsync($"/api/exams/{examId}/subjects",
            SubjectPayload(classId, subjectId, maxMarks));
        paper.EnsureSuccessStatusCode();
        var examSubjectId = (await ReadJson(paper)).GetProperty("ExamSubjectId").GetInt32();

        return (examId, examSubjectId, classId, students);
    }

    /* --------------------------- exam CRUD --------------------------- */

    [Fact]
    public async Task Exam_Is_Created_With_Its_Dates_And_Pass_Mark()
    {
        await ResetAsync();
        var yearId = await AnyAcademicYearIdAsync();

        using var client = Admin;
        using var response = await client.PostAsJsonAsync("/api/exams",
            ExamPayload("EX-Final", yearId, new DateTime(2026, 5, 1), new DateTime(2026, 5, 20), 45m));
        response.EnsureSuccessStatusCode();
        var examId = (await ReadJson(response)).GetProperty("ExamId").GetInt32();

        // The old list was SELECT *, so it showed no year and no pass mark and
        // the marking screen had no threshold to grade against.
        using var listed = await client.GetAsync("/api/exams");
        listed.EnsureSuccessStatusCode();

        var row = (await ReadJson(listed)).EnumerateArray()
            .First(r => r.GetProperty("Id").GetInt32() == examId);

        Assert.Equal("EX-Final", row.GetProperty("Title").GetString());
        Assert.Equal(45m, row.GetProperty("PassingMarks").GetDecimal());
        // A DATE column still serialises with a midnight time component, the same
        // as the attendance session date, so the day is what is compared.
        Assert.StartsWith("2026-05-01", row.GetProperty("StartDate").GetString());
        Assert.StartsWith("2026-05-20", row.GetProperty("EndDate").GetString());
        Assert.NotNull(row.GetProperty("AcademicYearName").GetString());
    }

    [Fact]
    public async Task Exam_Title_Is_Unique_Per_Academic_Year()
    {
        await ResetAsync();
        var yearId = await AnyAcademicYearIdAsync();

        using var client = Admin;
        using var first = await client.PostAsJsonAsync("/api/exams", ExamPayload("EX-Dupe", yearId));
        first.EnsureSuccessStatusCode();

        // Two exams with the same name in one year are indistinguishable on a
        // transcript, so the second has to be refused.
        using var second = await client.PostAsJsonAsync("/api/exams", ExamPayload("EX-Dupe", yearId));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("already exists", (await ReadJson(second)).GetProperty("Message").GetString()!);
    }

    [Fact]
    public async Task Exam_Rejects_A_Window_That_Ends_Before_It_Starts()
    {
        await ResetAsync();
        var yearId = await AnyAcademicYearIdAsync();

        using var client = Admin;
        using var response = await client.PostAsJsonAsync("/api/exams",
            ExamPayload("EX-Backwards", yearId, new DateTime(2026, 6, 20), new DateTime(2026, 6, 1)));

        // Always-empty exam, and the list showed both dates as if scheduled.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("before the start", (await ReadJson(response)).GetProperty("Message").GetString()!);
    }

    [Fact]
    public async Task Exam_Rejects_A_Pass_Mark_Outside_A_Percentage()
    {
        await ResetAsync();
        var yearId = await AnyAcademicYearIdAsync();

        using var client = Admin;
        using var response = await client.PostAsJsonAsync("/api/exams",
            ExamPayload("EX-Impossible", yearId, passingMarks: 140m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Exam_Is_Updated_In_Place()
    {
        await ResetAsync();
        var yearId = await AnyAcademicYearIdAsync();

        using var client = Admin;
        using var created = await client.PostAsJsonAsync("/api/exams", ExamPayload("EX-Before", yearId));
        created.EnsureSuccessStatusCode();
        var examId = (await ReadJson(created)).GetProperty("ExamId").GetInt32();

        using var updated = await client.PutAsJsonAsync($"/api/exams/{examId}",
            ExamPayload("EX-After", yearId, new DateTime(2026, 7, 1), new DateTime(2026, 7, 5), 50m));
        updated.EnsureSuccessStatusCode();

        using var detail = await client.GetAsync($"/api/exams/{examId}");
        detail.EnsureSuccessStatusCode();
        var exam = (await ReadJson(detail)).GetProperty("Exam");

        Assert.Equal("EX-After", exam.GetProperty("Title").GetString());
        Assert.Equal(50m, exam.GetProperty("PassingMarks").GetDecimal());
    }

    [Fact]
    public async Task Exam_Delete_Is_Refused_While_Subjects_Remain()
    {
        await ResetAsync();
        var (examId, _, _, _) = await MakePaperAsync("EX-Guarded");

        using var client = Admin;
        using var response = await client.DeleteAsync($"/api/exams/{examId}");

        // The paper's marks cascade from it, so a blanket delete would discard
        // results. The count tells the admin what to remove first.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("subject", (await ReadJson(response)).GetProperty("Message").GetString()!);
    }

    [Fact]
    public async Task Exam_Delete_Removes_An_Empty_Exam()
    {
        await ResetAsync();
        var yearId = await AnyAcademicYearIdAsync();

        using var client = Admin;
        using var created = await client.PostAsJsonAsync("/api/exams", ExamPayload("EX-Disposable", yearId));
        created.EnsureSuccessStatusCode();
        var examId = (await ReadJson(created)).GetProperty("ExamId").GetInt32();

        using var deleted = await client.DeleteAsync($"/api/exams/{examId}");
        deleted.EnsureSuccessStatusCode();

        using var gone = await client.GetAsync($"/api/exams/{examId}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Deleting_A_Class_Is_Refused_While_It_Holds_Exam_Subjects()
    {
        await ResetAsync();
        var (examId, _, classId, students) = await MakePaperAsync("EX-Class-Guard");

        // Empty the class first: a class with students in it is refused by the
        // enrollment guard, and this needs to reach the exam-subject guard.
        await using (var conn = new NpgsqlConnection(ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("DELETE FROM Enrollments WHERE ClassId = @c",
                new { c = classId });
        }

        using var client = Admin;
        using var response = await client.DeleteAsync($"/api/academic/classes/{classId}");

        // ExamSubjects.ClassId is ON DELETE RESTRICT. Letting the database decide
        // would answer with a bare foreign key error, and the marks under those
        // papers are results, not a side effect of tidying a class up.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("exam subject", (await ReadJson(response)).GetProperty("Message").GetString()!);
    }

    /* ------------------------- exam subjects ------------------------- */

    [Fact]
    public async Task Subject_Is_Added_To_An_Exam_For_A_Class()
    {
        await ResetAsync();
        var (examId, _, classId, _) = await MakePaperAsync("EX-Paper");

        using var client = Admin;
        using var detail = await client.GetAsync($"/api/exams/{examId}");
        detail.EnsureSuccessStatusCode();

        var subjects = (await ReadJson(detail)).GetProperty("Subjects").EnumerateArray().ToList();
        var paper = Assert.Single(subjects);

        // This write path did not exist at all, so the table could only ever be
        // empty and the whole marking flow was unreachable.
        Assert.Equal(classId, paper.GetProperty("ClassId").GetInt32());
        Assert.Equal(100m, paper.GetProperty("MaxMarks").GetDecimal());
        Assert.Equal("EX-Subj", paper.GetProperty("SubjectName").GetString());
    }

    [Fact]
    public async Task Subject_Rejects_Zero_Max_Marks()
    {
        await ResetAsync();
        var (classId, _, _) = await MakeClassAsync();
        var subjectId = await AnySubjectIdAsync();
        var yearId = await AnyAcademicYearIdAsync();

        using var client = Admin;
        using var created = await client.PostAsJsonAsync("/api/exams", ExamPayload("EX-ZeroMax", yearId));
        created.EnsureSuccessStatusCode();
        var examId = (await ReadJson(created)).GetProperty("ExamId").GetInt32();

        using var response = await client.PostAsJsonAsync($"/api/exams/{examId}/subjects",
            SubjectPayload(classId, subjectId, 0m));

        // MaxMarks is what the grade percentage divides by, so zero threw a
        // divide by zero rather than rejecting the input.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("greater than zero", (await ReadJson(response)).GetProperty("Message").GetString()!);
    }

    [Fact]
    public async Task Subject_Is_Refused_Twice_For_The_Same_Exam_Class_And_Subject()
    {
        await ResetAsync();
        var (examId, _, classId, _) = await MakePaperAsync("EX-Twice");
        var subjectId = await AnySubjectIdAsync();

        using var client = Admin;
        using var response = await client.PostAsJsonAsync($"/api/exams/{examId}/subjects",
            SubjectPayload(classId, subjectId));

        // Two rows for the same triple meant two independent sets of marks for
        // one sitting, and the transcript depended on which row was read.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task One_Exam_Can_Span_Classes()
    {
        await ResetAsync();
        var (firstClass, _, _) = await MakeClassAsync("EX-Junior");
        var (secondClass, _, _) = await MakeClassAsync("EX-Senior");
        var subjectId = await AnySubjectIdAsync();
        var yearId = await AnyAcademicYearIdAsync();

        using var client = Admin;
        using var created = await client.PostAsJsonAsync("/api/exams", ExamPayload("EX-Shared", yearId));
        created.EnsureSuccessStatusCode();
        var examId = (await ReadJson(created)).GetProperty("ExamId").GetInt32();

        using var first = await client.PostAsJsonAsync($"/api/exams/{examId}/subjects",
            SubjectPayload(firstClass, subjectId));
        first.EnsureSuccessStatusCode();

        // ClassId sits on the subject, not the exam, so one paper sat by several
        // grades does not need the exam duplicated per class.
        using var second = await client.PostAsJsonAsync($"/api/exams/{examId}/subjects",
            SubjectPayload(secondClass, subjectId));
        second.EnsureSuccessStatusCode();

        using var detail = await client.GetAsync($"/api/exams/{examId}");
        detail.EnsureSuccessStatusCode();
        var subjects = (await ReadJson(detail)).GetProperty("Subjects").EnumerateArray().ToList();
        Assert.Equal(2, subjects.Count);
        Assert.Contains(subjects, s => s.GetProperty("ClassId").GetInt32() == firstClass);
        Assert.Contains(subjects, s => s.GetProperty("ClassId").GetInt32() == secondClass);
    }

    [Fact]
    public async Task Subject_Delete_Is_Refused_Once_Marks_Exist()
    {
        await ResetAsync();
        var (examId, examSubjectId, _, students) = await MakePaperAsync("EX-Paper-Guard");

        using var client = Admin;
        using var saved = await client.PutAsJsonAsync($"/api/exams/subjects/{examSubjectId}/marks",
            new { Marks = students.Select(s => Mark(s, 80m)).ToArray() });
        saved.EnsureSuccessStatusCode();

        using var response = await client.DeleteAsync($"/api/exams/{examId}/subjects/{examSubjectId}");

        // Marks cascade from the paper, so the check has to happen first.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("mark", (await ReadJson(response)).GetProperty("Message").GetString()!);
    }

    /* ----------------------------- marks ----------------------------- */

    [Fact]
    public async Task Roster_Lists_The_Papers_Class_With_Stored_Marks_Attached()
    {
        await ResetAsync();
        var (_, examSubjectId, classId, students) = await MakePaperAsync("EX-Roster");

        using var client = Admin;
        using var response = await client.GetAsync($"/api/exams/subjects/{examSubjectId}/roster");
        response.EnsureSuccessStatusCode();
        var body = await ReadJson(response);

        var listed = body.GetProperty("Students").EnumerateArray()
            .Select(s => s.GetProperty("StudentId").GetInt32())
            .ToList();
        Assert.Equal(students.Count, listed.Count);
        foreach (var id in students) Assert.Contains(id, listed);

        Assert.Equal(classId, body.GetProperty("ClassId").GetInt32());
        Assert.Equal(100m, body.GetProperty("MaxMarks").GetDecimal());
        // The pass mark has to reach the screen, or the teacher cannot see the
        // threshold the server grades against.
        Assert.Equal(40m, body.GetProperty("PassingPercentage").GetDecimal());

        // Nothing marked yet, so marks have to be null rather than defaulting to
        // zero and reading as a real fail.
        foreach (var row in body.GetProperty("Students").EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Null, row.GetProperty("MarksObtained").ValueKind);
            Assert.Equal(JsonValueKind.Null, row.GetProperty("Grade").ValueKind);
        }
    }

    [Fact]
    public async Task Roster_Shows_Stored_Marks_On_A_Second_Load()
    {
        await ResetAsync();
        var (_, examSubjectId, _, students) = await MakePaperAsync("EX-Rereload");

        using var client = Admin;
        using var saved = await client.PutAsJsonAsync($"/api/exams/subjects/{examSubjectId}/marks",
            new { Marks = students.Select(s => Mark(s, 75m)).ToList() });
        saved.EnsureSuccessStatusCode();

        using var response = await client.GetAsync($"/api/exams/subjects/{examSubjectId}/roster");
        response.EnsureSuccessStatusCode();

        // Re-marking starts from what is stored rather than asking the teacher to
        // retype a full roster.
        foreach (var row in (await ReadJson(response)).GetProperty("Students").EnumerateArray())
        {
            Assert.Equal(75m, row.GetProperty("MarksObtained").GetDecimal());
            Assert.Equal("B", row.GetProperty("Grade").GetString());
        }
    }

    [Fact]
    public async Task Whole_Roster_Is_Saved_In_One_Call_And_Graded()
    {
        await ResetAsync();
        var (_, examSubjectId, _, students) = await MakePaperAsync("EX-Marks", maxMarks: 50m, studentCount: 3);

        using var client = Admin;
        using var response = await client.PutAsJsonAsync($"/api/exams/subjects/{examSubjectId}/marks",
            new { Marks = new[] { Mark(students[0], 50m), Mark(students[1], 35m), Mark(students[2], 10m) } });
        response.EnsureSuccessStatusCode();

        var body = await ReadJson(response);
        Assert.Equal(3, body.GetProperty("Saved").GetInt32());

        var graded = body.GetProperty("Marks").EnumerateArray()
            .ToDictionary(m => m.GetProperty("StudentId").GetInt32(), m => m);

        // 100/50 = 100%, 70/50 = 70%, 20/50 = 20% against a 40% pass mark.
        Assert.Equal(100m, graded[students[0]].GetProperty("Percentage").GetDecimal());
        Assert.True(graded[students[0]].GetProperty("Passed").GetBoolean());
        Assert.Equal("A+", graded[students[0]].GetProperty("Grade").GetString());

        Assert.Equal(70m, graded[students[1]].GetProperty("Percentage").GetDecimal());
        Assert.Equal("B", graded[students[1]].GetProperty("Grade").GetString());
        Assert.True(graded[students[1]].GetProperty("Passed").GetBoolean());

        Assert.Equal(20m, graded[students[2]].GetProperty("Percentage").GetDecimal());
        Assert.Equal("F", graded[students[2]].GetProperty("Grade").GetString());
        Assert.False(graded[students[2]].GetProperty("Passed").GetBoolean());
    }

    [Fact]
    public async Task Marks_Are_Rejected_Above_Max_Marks()
    {
        await ResetAsync();
        var (_, examSubjectId, _, students) = await MakePaperAsync("EX-Over", maxMarks: 100m);

        using var client = Admin;
        using var response = await client.PutAsJsonAsync($"/api/exams/subjects/{examSubjectId}/marks",
            new { Marks = new[] { Mark(students[0], 150m) } });

        // Nothing checked this, and MaxMarks is the divisor, so a mark above it
        // graded as a percentage above 100.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("outside 0 to 100", (await ReadJson(response)).GetProperty("Message").GetString()!);
    }

    [Fact]
    public async Task Marks_Are_Rejected_Below_Zero()
    {
        await ResetAsync();
        var (_, examSubjectId, _, students) = await MakePaperAsync("EX-Negative");

        using var client = Admin;
        using var response = await client.PutAsJsonAsync($"/api/exams/subjects/{examSubjectId}/marks",
            new { Marks = new[] { Mark(students[0], -5m) } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_Rejected_Roster_Leaves_Nothing_Behind()
    {
        await ResetAsync();
        var (_, examSubjectId, _, students) = await MakePaperAsync("EX-Atomic", studentCount: 2);

        using var client = Admin;

        // One good row and one stray. The stray is rejected after the good one has
        // been written, so this only holds if the whole save is one transaction:
        // per-student calls would have left the first mark committed.
        using var response = await client.PutAsJsonAsync($"/api/exams/subjects/{examSubjectId}/marks",
            new { Marks = new[] { Mark(students[0], 90m), Mark(999999, 90m) } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM marks WHERE examsubjectid = @s", new { s = examSubjectId }));
    }

    [Fact]
    public async Task Marks_Are_Rejected_For_A_Student_From_Another_Class()
    {
        await ResetAsync();
        var (_, examSubjectId, _, students) = await MakePaperAsync("EX-Stranger");

        // A real second account that is not enrolled in this paper's class. It
        // cannot be one of the fixture's own students: they can only sit in one
        // class at a time, so borrowing one would move them into this class and
        // make the test pass for the wrong reason.
        var strangers = await MakeStudentsAsync(1, "stranger");

        using var client = Admin;
        using var response = await client.PutAsJsonAsync($"/api/exams/subjects/{examSubjectId}/marks",
            new { Marks = new[] { Mark(strangers[0], 90m) } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not enrolled", (await ReadJson(response)).GetProperty("Message").GetString()!);
    }

    [Fact]
    public async Task Re_Saving_Marks_Updates_Them_Instead_Of_Duplicating()
    {
        await ResetAsync();
        var (_, examSubjectId, _, students) = await MakePaperAsync("EX-Correct", studentCount: 2);

        using var client = Admin;
        using var first = await client.PutAsJsonAsync($"/api/exams/subjects/{examSubjectId}/marks",
            new
            {
                Marks = new[]
                {
                    Mark(students[0], 40m, "original remark"),
                    Mark(students[1], 40m),
                },
            });
        first.EnsureSuccessStatusCode();

        using var second = await client.PutAsJsonAsync($"/api/exams/subjects/{examSubjectId}/marks",
            new { Marks = new[] { Mark(students[0], 95m) } });
        second.EnsureSuccessStatusCode();

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        // In-place, so a correction cannot duplicate a student and inflate the
        // row count the report reads.
        Assert.Equal(2, await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM marks WHERE examsubjectid = @s", new { s = examSubjectId }));

        var corrected = await conn.QuerySingleAsync<(decimal Obtained, string Grade)>(
            "SELECT marksobtained, grade FROM marks WHERE examsubjectid = @s AND studentid = @st",
            new { s = examSubjectId, st = students[0] });

        Assert.Equal(95m, corrected.Obtained);
        Assert.Equal("A+", corrected.Grade);

        // The save replaces the whole roster, so the remark in the payload is what
        // is stored — the same contract as the attendance screen, which pre-fills
        // remarks from the roster so a re-save carries them back.
        Assert.Equal("", await conn.QuerySingleAsync<string>(
            "SELECT remarks FROM marks WHERE examsubjectid = @s AND studentid = @st",
            new { s = examSubjectId, st = students[0] }));
    }

    [Fact]
    public async Task Pass_Fail_Follows_The_Exams_Pass_Mark()
    {
        await ResetAsync();
        var (examId, examSubjectId, _, students) = await MakePaperAsync("EX-Threshold", maxMarks: 100m);

        // Raise the pass mark to 80% after the paper exists.
        using (var client = Admin)
        {
            var yearId = await AnyAcademicYearIdAsync();
            using var updated = await client.PutAsJsonAsync($"/api/exams/{examId}",
                ExamPayload("EX-Threshold", yearId, new DateTime(2026, 4, 1), new DateTime(2026, 4, 10), 80m));
            updated.EnsureSuccessStatusCode();
        }

        using var client2 = Admin;
        using var response = await client2.PutAsJsonAsync($"/api/exams/subjects/{examSubjectId}/marks",
            new { Marks = new[] { Mark(students[0], 75m) } });
        response.EnsureSuccessStatusCode();

        var marked = (await ReadJson(response)).GetProperty("Marks").EnumerateArray().Single();

        // 75% graded B under the old bands but fails an 80% pass mark. PassingMarks
        // was stored and never read, so every student passed by default.
        Assert.Equal("B", marked.GetProperty("Grade").GetString());
        Assert.False(marked.GetProperty("Passed").GetBoolean());
    }

    [Fact]
    public async Task Empty_Roster_Save_Is_Rejected()
    {
        await ResetAsync();
        var (_, examSubjectId, _, _) = await MakePaperAsync("EX-Empty");

        using var client = Admin;
        using var response = await client.PutAsJsonAsync($"/api/exams/subjects/{examSubjectId}/marks",
            new { Marks = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Saving_Marks_For_An_Unknown_Paper_Returns_404()
    {
        using var client = Admin;
        using var response = await client.PutAsJsonAsync("/api/exams/subjects/999999/marks",
            new { Marks = new[] { Mark(_fixture.LinkedStudentId, 50m) } });

        // The old handler read MaxMarks from the paper with a scalar that threw on
        // no row, so an unknown id surfaced as a 500.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /* ----------------------------- results ----------------------------- */

    [Fact]
    public async Task Student_Reads_Their_Own_Results_Without_Knowing_Their_Id()
    {
        await ResetAsync();
        var (_, examSubjectId, _, students) = await MakePaperAsync("EX-Transcript", maxMarks: 100m, studentCount: 2);

        using var admin = Admin;
        using var saved = await admin.PutAsJsonAsync($"/api/exams/subjects/{examSubjectId}/marks",
            new
            {
                Marks = new[]
                {
                    Mark(students[0], 90m),
                    Mark(students[1], 50m),
                },
            });
        saved.EnsureSuccessStatusCode();

        using var student = Student;
        using var response = await student.GetAsync("/api/exams/mine");
        response.EnsureSuccessStatusCode();

        var rows = (await ReadJson(response)).EnumerateArray().ToList();
        var exam = Assert.Single(rows);

        Assert.Equal("EX-Transcript", exam.GetProperty("ExamTitle").GetString());
        Assert.Equal(1, exam.GetProperty("SubjectCount").GetInt32());
        Assert.Equal(40m, exam.GetProperty("PassingPercentage").GetDecimal());
        Assert.Equal(90m, exam.GetProperty("TotalObtained").GetDecimal());
        Assert.Equal(100m, exam.GetProperty("TotalMax").GetDecimal());
        Assert.Equal(90m, exam.GetProperty("OverallPercentage").GetDecimal());
        Assert.True(exam.GetProperty("Passed").GetBoolean());
    }

    [Fact]
    public async Task Parent_And_Staff_May_Not_Use_The_Own_Results_Endpoint()
    {
        using var parent = Parent;
        using var parentResponse = await parent.GetAsync("/api/exams/mine");
        Assert.Equal(HttpStatusCode.Forbidden, parentResponse.StatusCode);

        using var teacher = Teacher;
        using var teacherResponse = await teacher.GetAsync("/api/exams/mine");
        Assert.Equal(HttpStatusCode.Forbidden, teacherResponse.StatusCode);
    }

    [Fact]
    public async Task Student_May_Read_Only_Their_Own_Results()
    {
        await ResetAsync();
        var (_, examSubjectId, _, students) = await MakePaperAsync("EX-Scoped");

        using var admin = Admin;
        using var saved = await admin.PutAsJsonAsync($"/api/exams/subjects/{examSubjectId}/marks",
            new { Marks = students.Select(s => Mark(s, 70m)).ToList() });
        saved.EnsureSuccessStatusCode();

        using var student = Student;
        using var own = await student.GetAsync($"/api/exams/student/{_fixture.LinkedStudentId}");
        own.EnsureSuccessStatusCode();

        // The linked student is the fixture's own account, so only a different id
        // can be probed here.
        using var other = await student.GetAsync($"/api/exams/student/{_fixture.UnlinkedStudentId}");
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
    }

    [Fact]
    public async Task Parent_May_Read_A_Linked_Child_But_Not_An_Unlinked_Student()
    {
        await ResetAsync();
        var (_, examSubjectId, _, students) = await MakePaperAsync("EX-Parent");

        using var admin = Admin;
        using var saved = await admin.PutAsJsonAsync($"/api/exams/subjects/{examSubjectId}/marks",
            new { Marks = students.Select(s => Mark(s, 70m)).ToList() });
        saved.EnsureSuccessStatusCode();

        using var parent = Parent;
        using var linked = await parent.GetAsync($"/api/exams/student/{_fixture.LinkedStudentId}");
        linked.EnsureSuccessStatusCode();

        // The fixture deliberately leaves the second student unlinked so this
        // branch is reachable.
        using var unlinked = await parent.GetAsync($"/api/exams/student/{_fixture.UnlinkedStudentId}");
        Assert.Equal(HttpStatusCode.Forbidden, unlinked.StatusCode);
    }

    [Fact]
    public async Task Transcript_Is_Grouped_Per_Exam_With_Its_Own_Pass_Mark()
    {
        await ResetAsync();
        var yearId = await AnyAcademicYearIdAsync();
        var subjectId = await AnySubjectIdAsync();
        var (classId, _, students) = await MakeClassAsync("EX-Grouped");

        using var client = Admin;

        // Two exams with different pass marks. The rollup has to read the mark
        // per exam: applying one exam's threshold to both would grade a
        // transcript against the wrong bar.
        foreach (var (title, passing) in new[] { ("EX-Loose", 30m), ("EX-Strict", 85m) })
        {
            using var created = await client.PostAsJsonAsync("/api/exams",
                ExamPayload(title, yearId, new DateTime(2026, 8, 1), new DateTime(2026, 8, 9), passing));
            created.EnsureSuccessStatusCode();
            var examId = (await ReadJson(created)).GetProperty("ExamId").GetInt32();

            using var paper = await client.PostAsJsonAsync($"/api/exams/{examId}/subjects",
                SubjectPayload(classId, subjectId, 100m));
            paper.EnsureSuccessStatusCode();
            var examSubjectId = (await ReadJson(paper)).GetProperty("ExamSubjectId").GetInt32();

            using var saved = await client.PutAsJsonAsync($"/api/exams/subjects/{examSubjectId}/marks",
                new { Marks = students.Select(s => Mark(s, 80m)).ToList() });
            saved.EnsureSuccessStatusCode();
        }

        using var response = await client.GetAsync($"/api/exams/student/{students[0]}");
        response.EnsureSuccessStatusCode();

        var rows = (await ReadJson(response)).EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);

        var strict = rows.Single(r => r.GetProperty("ExamTitle").GetString() == "EX-Strict");
        var loose = rows.Single(r => r.GetProperty("ExamTitle").GetString() == "EX-Loose");

        Assert.Equal(85m, strict.GetProperty("PassingPercentage").GetDecimal());
        Assert.Equal(30m, loose.GetProperty("PassingPercentage").GetDecimal());

        // Same 80% mark, opposite verdicts, because the pass marks differ.
        Assert.Equal(80m, strict.GetProperty("OverallPercentage").GetDecimal());
        Assert.False(strict.GetProperty("Passed").GetBoolean());
        Assert.True(loose.GetProperty("Passed").GetBoolean());
    }

    /* --------------------------- cleanup --------------------------- */

    [Fact]
    public async Task Deleting_A_Paper_Takes_Its_Marks_With_It()
    {
        await ResetAsync();
        var (examId, examSubjectId, _, students) = await MakePaperAsync("EX-Cascade");

        using var client = Admin;
        using var saved = await client.PutAsJsonAsync($"/api/exams/subjects/{examSubjectId}/marks",
            new { Marks = students.Select(s => Mark(s, 60m)).ToList() });
        saved.EnsureSuccessStatusCode();

        // Clear the marks first: the delete is refused while any exist, which is
        // the point of the guard.
        await using (var conn = new NpgsqlConnection(ConnectionString))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("DELETE FROM marks WHERE examsubjectid = @s", new { s = examSubjectId });
        }

        using var deleted = await client.DeleteAsync($"/api/exams/{examId}/subjects/{examSubjectId}");
        deleted.EnsureSuccessStatusCode();

        await using var check = new NpgsqlConnection(ConnectionString);
        await check.OpenAsync();
        Assert.Equal(0, await check.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM examsubjects WHERE id = @s", new { s = examSubjectId }));
        Assert.Equal(0, await check.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM marks WHERE examsubjectid = @s", new { s = examSubjectId }));
    }
}
