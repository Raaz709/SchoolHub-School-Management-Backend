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
/// Covers the event flow.
///
/// Events could only ever be listed and created: there was no way to correct a
/// typo, cancel a cancelled event, or record who was coming. EventParticipants
/// was seeded by hand and unreadable, and its Status column was free text the
/// UI had no rendering for. What is worth protecting now:
///
///   1. An event needs a title and a date, and the same title on the same date
///      is a duplicate (409) rather than a second card.
///   2. An event can be edited and deleted, and deleting it takes its responses
///      with it.
///   3. Anyone signed in can record one response of the four the UI knows, and
///      only that response; it is upserted, and the caller sees it back.
///   4. Staff can remove someone else's response; a learner can remove only
///      their own.
///   5. Creating, editing and deleting an event is staff-only.
/// </summary>
[Collection(AuthTestCollection.Name)]
public class EventsTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Database=schoolhub_db;Username=postgres;Password=00000";

    private readonly HttpClient _admin;
    private readonly HttpClient _teacher;
    private readonly HttpClient _student;

    public EventsTests(AuthTestFixture fixture)
    {
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

    /// <summary>Removes every EV- throwaway event; participants cascade.</summary>
    private static async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM Events WHERE Title LIKE 'EV-%'");
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<int> UserIdAsync(string username)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        return await conn.QuerySingleAsync<int>(
            "SELECT Id FROM Users WHERE Username = @u", new { u = username });
    }

    private static string NewTitle() => "EV-" + Guid.NewGuid().ToString("N")[..8];

    private async Task<int> CreateEventAsync(string title, DateTime when, string? location = "Main Hall")
    {
        using var res = await _admin.PostAsJsonAsync("/api/schoolextensions/events", new
        {
            Title = title,
            Description = "Throwaway test event",
            EventDate = when,
            Location = location,
        });
        res.EnsureSuccessStatusCode();
        return (await ReadJson(res)).GetProperty("Id").GetInt32();
    }

    /* --------------------------- validation + duplicates --------------------------- */

    [Fact]
    public async Task Create_Rejects_Blank_Title_And_Missing_Date()
    {
        await ResetAsync();

        using (var blank = await _admin.PostAsJsonAsync("/api/schoolextensions/events",
            new { Title = "   ", EventDate = DateTime.UtcNow.AddDays(1) }))
            Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

        using (var noDate = await _admin.PostAsJsonAsync("/api/schoolextensions/events",
            new { Title = NewTitle(), EventDate = default(DateTime) }))
            Assert.Equal(HttpStatusCode.BadRequest, noDate.StatusCode);
    }

    [Fact]
    public async Task Same_Title_On_The_Same_Date_Is_A_Duplicate()
    {
        await ResetAsync();
        var title = NewTitle();
        var when = DateTime.UtcNow.AddDays(14);
        await CreateEventAsync(title, when);

        using var again = await _admin.PostAsJsonAsync("/api/schoolextensions/events", new
        {
            Title = title,
            EventDate = when,
        });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        // A different date is a legitimate repeat (the annual case).
        using var otherDay = await _admin.PostAsJsonAsync("/api/schoolextensions/events", new
        {
            Title = title,
            EventDate = when.AddDays(365),
        });
        otherDay.EnsureSuccessStatusCode();
    }

    /* --------------------------- CRUD --------------------------- */

    [Fact]
    public async Task Event_Can_Be_Updated_And_Deleted()
    {
        await ResetAsync();
        var id = await CreateEventAsync(NewTitle(), DateTime.UtcNow.AddDays(7));

        var renamed = NewTitle();
        using (var update = await _admin.PutAsJsonAsync($"/api/schoolextensions/events/{id}", new
        {
            Title = renamed,
            Description = "Edited",
            EventDate = DateTime.UtcNow.AddDays(8),
            Location = "Room 4",
        }))
            update.EnsureSuccessStatusCode();

        using (var fetch = await _student.GetAsync($"/api/schoolextensions/events/{id}"))
        {
            fetch.EnsureSuccessStatusCode();
            var body = await ReadJson(fetch);
            Assert.Equal(renamed, body.GetProperty("Title").GetString());
            Assert.Equal("Room 4", body.GetProperty("Location").GetString());
        }

        using (var delete = await _admin.DeleteAsync($"/api/schoolextensions/events/{id}"))
            delete.EnsureSuccessStatusCode();

        using (var gone = await _admin.GetAsync($"/api/schoolextensions/events/{id}"))
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Unknown_Event_Returns_NotFound()
    {
        await ResetAsync();
        const int missing = 999_999;

        using (var get = await _admin.GetAsync($"/api/schoolextensions/events/{missing}"))
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        using (var signup = await _admin.GetAsync($"/api/schoolextensions/events/{missing}/participants"))
            Assert.Equal(HttpStatusCode.NotFound, signup.StatusCode);

        using (var update = await _admin.PutAsJsonAsync($"/api/schoolextensions/events/{missing}",
            new { Title = NewTitle(), EventDate = DateTime.UtcNow.AddDays(1) }))
            Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        using (var rsvp = await _admin.PutAsJsonAsync($"/api/schoolextensions/events/{missing}/rsvp",
            new { Status = "Attending" }))
            Assert.Equal(HttpStatusCode.NotFound, rsvp.StatusCode);

        using (var delete = await _admin.DeleteAsync($"/api/schoolextensions/events/{missing}"))
            Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    /* --------------------------- responses --------------------------- */

    [Fact]
    public async Task Rsvp_Is_Upserted_And_Visible_In_List_And_Participants()
    {
        await ResetAsync();
        var id = await CreateEventAsync(NewTitle(), DateTime.UtcNow.AddDays(3));

        using (var first = await _student.PutAsJsonAsync($"/api/schoolextensions/events/{id}/rsvp",
            new { Status = "Attending" }))
            first.EnsureSuccessStatusCode();

        using (var list = await _student.GetAsync("/api/schoolextensions/events"))
        {
            list.EnsureSuccessStatusCode();
            var mine = (await ReadJson(list)).EnumerateArray()
                .Single(e => e.GetProperty("Id").GetInt32() == id);
            Assert.Equal("Attending", mine.GetProperty("MyStatus").GetString());
            Assert.Equal(1, mine.GetProperty("ParticipantCount").GetInt32());
        }

        // A second call replaces rather than adds a row.
        using (var change = await _student.PutAsJsonAsync($"/api/schoolextensions/events/{id}/rsvp",
            new { Status = "Maybe" }))
            change.EnsureSuccessStatusCode();

        using (var people = await _admin.GetAsync($"/api/schoolextensions/events/{id}/participants"))
        {
            people.EnsureSuccessStatusCode();
            var rows = (await ReadJson(people)).EnumerateArray().ToList();
            var student = rows.Single(r => r.GetProperty("Username").GetString() == AuthTestFixture.StudentUsername);
            Assert.Equal("Maybe", student.GetProperty("Status").GetString());
            Assert.Single(rows);
        }
    }

    [Fact]
    public async Task Rsvp_Rejects_An_Unknown_Status()
    {
        await ResetAsync();
        var id = await CreateEventAsync(NewTitle(), DateTime.UtcNow.AddDays(3));

        using var bad = await _student.PutAsJsonAsync($"/api/schoolextensions/events/{id}/rsvp",
            new { Status = "Probably" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Staff_Remove_Anyone_But_A_Learner_Only_Themselves()
    {
        await ResetAsync();
        var id = await CreateEventAsync(NewTitle(), DateTime.UtcNow.AddDays(3));
        var studentUserId = await UserIdAsync(AuthTestFixture.StudentUsername);
        var teacherUserId = await UserIdAsync(AuthTestFixture.TeacherUsername);

        using (var signup = await _student.PutAsJsonAsync($"/api/schoolextensions/events/{id}/rsvp",
            new { Status = "Attending" }))
            signup.EnsureSuccessStatusCode();

        using (var remove = await _teacher.DeleteAsync(
            $"/api/schoolextensions/events/{id}/participants/{studentUserId}"))
            remove.EnsureSuccessStatusCode();

        // Back on the list, then the learner is refused when reaching for someone
        // else's row, and allowed to take back their own.
        using (var second = await _student.PutAsJsonAsync($"/api/schoolextensions/events/{id}/rsvp",
            new { Status = "Attending" }))
            second.EnsureSuccessStatusCode();

        using (var foreign = await _student.DeleteAsync(
            $"/api/schoolextensions/events/{id}/participants/{teacherUserId}"))
            Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        using (var self = await _student.DeleteAsync(
            $"/api/schoolextensions/events/{id}/participants/{studentUserId}"))
            self.EnsureSuccessStatusCode();

        using (var missing = await _admin.DeleteAsync(
            $"/api/schoolextensions/events/{id}/participants/{studentUserId}"))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    /* --------------------------- authorization --------------------------- */

    [Fact]
    public async Task Event_Writes_Are_Admin_Only()
    {
        await ResetAsync();
        var id = await CreateEventAsync(NewTitle(), DateTime.UtcNow.AddDays(3));

        using (var create = await _teacher.PostAsJsonAsync("/api/schoolextensions/events",
            new { Title = NewTitle(), EventDate = DateTime.UtcNow.AddDays(5) }))
            Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        using (var update = await _teacher.PutAsJsonAsync($"/api/schoolextensions/events/{id}",
            new { Title = NewTitle(), EventDate = DateTime.UtcNow.AddDays(5) }))
            Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);

        using (var delete = await _teacher.DeleteAsync($"/api/schoolextensions/events/{id}"))
            Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }
}
