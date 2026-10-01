using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace SchoolHub.Tests;

/// <summary>
/// Creates a throwaway set of users — one per role, plus a second student that
/// the parent is deliberately not linked to — so the authorization tests are
/// self-contained and do not depend on hand-seeded dev accounts.
///
/// Everything is removed again in <see cref="DisposeAsync"/>, and the fixture
/// never asserts on pre-existing rows.
/// </summary>
public sealed class AuthTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "Test@Pass123";

    public const string AdminUsername = "t_admin";
    public const string TeacherUsername = "t_teacher";
    public const string StudentUsername = "t_student";
    public const string Student2Username = "t_student2";
    public const string ParentUsername = "t_parent";

    /// <summary>
    /// Created and removed by the student-write tests. It has to be in the
    /// cleanup list or those rows would outlive the run.
    /// </summary>
    public const string WritableStudentUsername = "w_student";

    private static readonly string[] Usernames =
    {
        AdminUsername, TeacherUsername, StudentUsername, Student2Username, ParentUsername,
        WritableStudentUsername,
    };

    private string ConnectionString =>
        "Host=localhost;Database=schoolhub_db;Username=postgres;Password=00000";

    public string AdminToken { get; private set; } = "";
    public string TeacherToken { get; private set; } = "";
    public string StudentToken { get; private set; } = "";
    public string ParentToken { get; private set; } = "";

    public int LinkedStudentId { get; private set; }
    public int UnlinkedStudentId { get; private set; }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        var hash = BCrypt.Net.BCrypt.HashPassword(Password);

        // Remove leftovers from an interrupted run before re-inserting.
        await DeleteTestUsersAsync(conn);

        var roleIds = (await conn.QueryAsync<(string, int)>(
            "SELECT name, id FROM roles WHERE name IN ('Admin','Teacher','Student','Parent')"))
            .ToDictionary(r => r.Item1, r => r.Item2, StringComparer.OrdinalIgnoreCase);

        foreach (var username in Usernames)
        {
            var role = username switch
            {
                AdminUsername => "Admin",
                TeacherUsername => "Teacher",
                StudentUsername or Student2Username => "Student",
                _ => "Parent",
            };

            var userId = await conn.ExecuteScalarAsync<int>(
                """
                INSERT INTO users (username, email, passwordhash, role, isactive)
                VALUES (@u, @e, @h, @r, TRUE)
                RETURNING id
                """,
                new { u = username, e = username + "@test.local", h = hash, r = role });

            await conn.ExecuteAsync(
                "INSERT INTO userroles (userid, roleid) VALUES (@u, @r)",
                new { u = userId, r = roleIds[role] });

            switch (role)
            {
                case "Teacher":
                    await conn.ExecuteAsync(
                        "INSERT INTO teachers (userid, employeecode) VALUES (@u, @c)",
                        new { u = userId, c = "T-" + username });
                    break;

                case "Student":
                    var roll = "R-" + username;
                    var studentId = await conn.ExecuteScalarAsync<int>(
                        "INSERT INTO students (userid, rollnumber) VALUES (@u, @r) RETURNING id",
                        new { u = userId, r = roll });
                    if (username == StudentUsername) LinkedStudentId = studentId;
                    else UnlinkedStudentId = studentId;
                    break;

                case "Parent":
                    var parentId = await conn.ExecuteScalarAsync<int>(
                        "INSERT INTO parents (userid) VALUES (@u) RETURNING id",
                        new { u = userId });
                    await conn.ExecuteAsync(
                        """
                        INSERT INTO studentparents (studentid, parentid, relationship)
                        VALUES (@s, @p, 'Mother')
                        """,
                        new { s = LinkedStudentId, p = parentId });
                    break;
            }
        }

        AdminToken = await LoginAsync(AdminUsername);
        TeacherToken = await LoginAsync(TeacherUsername);
        StudentToken = await LoginAsync(StudentUsername);
        ParentToken = await LoginAsync(ParentUsername);
    }

    public async Task DisposeAsync()
    {
        // Deleting the user cascades to userroles, students, parents, teachers
        // and studentparents, so no per-table teardown is needed.
        try
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await DeleteTestUsersAsync(conn);
        }
        finally
        {
            Dispose();
        }
    }

    private static async Task DeleteTestUsersAsync(NpgsqlConnection conn)
    {
        await conn.ExecuteAsync(
            "DELETE FROM users WHERE username = ANY(@u)", new { u = Usernames });
    }

    private async Task<string> LoginAsync(string username)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username, password = Password });
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("AccessToken").GetString()!;
    }

    public static HttpClient ClientFor(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// The test server only speaks plain HTTP, so the API must not try to
    /// redirect to an HTTPS port that does not exist.
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }
}
