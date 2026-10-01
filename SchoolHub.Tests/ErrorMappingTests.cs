using Dapper;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace SchoolHub.Tests;

/// <summary>
/// Ordinary client mistakes used to surface as HTTP 500, which made a duplicate
/// roll number or a submission to a deleted assignment look like an outage.
/// The contract now is: a bad request is a 4xx, and 500 is reserved for real
/// server faults.
///
/// Every case here cleans up after itself.
/// </summary>
[Collection(AuthTestCollection.Name)]
public class ErrorMappingTests
{
    private const string ConnectionString =
        "Host=localhost;Database=schoolhub_db;Username=postgres;Password=00000";

    private readonly AuthTestFixture _fixture;

    public ErrorMappingTests(AuthTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Duplicate_Unique_Value_Returns_409_Not_500()
    {
        using var client = AuthTestFixture.ClientFor(_fixture, _fixture.AdminToken);

        // Two teachers sharing an employee code violates the UNIQUE constraint.
        var first = await Post(client, "/api/teachers",
            new { firstName = "Dup", lastName = "One", email = "dup1@test.local", employeecode = "DUP-1" });
        first.EnsureSuccessStatusCode();
        var createdId = await ReadIdAsync(first);

        try
        {
            using var second = await Post(client, "/api/teachers",
                new { firstName = "Dup", lastName = "Two", email = "dup2@test.local", employeecode = "DUP-1" });

            Assert.True(
                second.StatusCode == HttpStatusCode.Conflict,
                $"expected 409 Conflict, got {(int)second.StatusCode} {second.StatusCode}.");
        }
        finally
        {
            await CleanupTeachersAsync("DUP-1");
            _ = createdId;
        }
    }

    [Fact]
    public async Task Reference_To_A_Missing_Row_Returns_4xx_Not_500()
    {
        using var client = AuthTestFixture.ClientFor(_fixture, _fixture.TeacherToken);

        // Student 999999 does not exist, so the class assignment must fail with
        // a 4xx rather than an unhandled database error.
        using var response = await Post(client, $"/api/students/{_fixture.LinkedStudentId}/assign-class",
            new { classId = 999999, sectionId = 999999 });

        AssertIn4xx(response, "assign class referencing a missing class/section");
    }

    [Fact]
    public async Task Malformed_Json_Body_Returns_400_Not_500()
    {
        using var client = AuthTestFixture.ClientFor(_fixture, _fixture.AdminToken);
        using var content = new StringContent("{ this is not json", System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/academic/classes", content);

        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"expected 400, got {(int)response.StatusCode} {response.StatusCode}.");
    }

    [Fact]
    public async Task Login_With_Wrong_Password_Returns_401_Not_500()
    {
        using var client = _fixture.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = AuthTestFixture.AdminUsername, password = "WrongPassword@1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Error_Bodies_Use_PascalCase_Like_The_Rest_Of_The_API()
    {
        using var client = AuthTestFixture.ClientFor(_fixture, _fixture.AdminToken);

        try
        {
            // Seed one teacher so the second POST collides.
            var seed = await Post(client, "/api/teachers",
                new { firstName = "Case", lastName = "Test", email = "case@test.local", employeecode = "CASE-1" });
            seed.EnsureSuccessStatusCode();

            using var conflict = await Post(client, "/api/teachers",
                new { firstName = "Case", lastName = "Test", email = "case2@test.local", employeecode = "CASE-1" });

            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

            var body = await conflict.Content.ReadAsStringAsync();
            Assert.Contains("\"StatusCode\"", body);
            Assert.Contains("\"Message\"", body);
        }
        finally
        {
            await CleanupTeachersAsync("CASE-1");
        }
    }

    private static void AssertIn4xx(HttpResponseMessage response, string what)
    {
        var code = (int)response.StatusCode;
        Assert.True(
            code is >= 400 and < 500,
            $"{what}: expected a 4xx, got {code} {response.StatusCode}.");
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body)
        => await client.PostAsJsonAsync(path, body);

    private static async Task<int> ReadIdAsync(HttpResponseMessage response)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("TeacherId", out var id) ? id.GetInt32() : 0;
    }

    private static async Task CleanupTeachersAsync(string employeeCode)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            """
            DELETE FROM users
            WHERE id IN (SELECT userid FROM teachers WHERE employeecode = @c)
            """,
            new { c = employeeCode });
    }
}
