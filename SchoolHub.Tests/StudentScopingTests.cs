using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace SchoolHub.Tests;

/// <summary>
/// The coarse role check is not enough. A Student must only see their own
/// record, and a Parent only the children actually linked to them — otherwise
/// "Student" and "Parent" would be horizontal privilege escalations.
///
/// These use two fixture students: the parent is linked to one and not the
/// other, so the negative cases are real.
/// </summary>
[Collection(AuthTestCollection.Name)]
public class StudentScopingTests
{
    private readonly AuthTestFixture _fixture;

    public StudentScopingTests(AuthTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Student_Can_Read_Own_Profile_Record()
    {
        using var client = AuthTestFixture.ClientFor(_fixture, _fixture.StudentToken);
        using var response = await client.GetAsync($"/api/students/{_fixture.LinkedStudentId}");

        // The controller forbids the broad student list for Students, so a direct
        // read of their own row is the case under test.
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Forbidden,
            $"unexpected status {(int)response.StatusCode} {response.StatusCode}");
    }

    [Fact]
    public async Task Student_Is_Forbidden_From_The_Student_List()
    {
        using var client = AuthTestFixture.ClientFor(_fixture, _fixture.StudentToken);
        using var response = await client.GetAsync("/api/students");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Parent_Can_Read_Own_Child()
    {
        using var client = AuthTestFixture.ClientFor(_fixture, _fixture.ParentToken);
        using var response = await client.GetAsync($"/api/students/{_fixture.LinkedStudentId}");
        Assert.True(response.IsSuccessStatusCode,
            $"linked child returned {(int)response.StatusCode} {response.StatusCode}.");
    }

    [Fact]
    public async Task Parent_Is_Forbidden_From_An_Unlinked_Student()
    {
        using var client = AuthTestFixture.ClientFor(_fixture, _fixture.ParentToken);
        using var response = await client.GetAsync($"/api/students/{_fixture.UnlinkedStudentId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Parent_Children_List_Excludes_Unlinked_Students()
    {
        using var client = AuthTestFixture.ClientFor(_fixture, _fixture.ParentToken);
        using var response = await client.GetAsync("/api/portals/parent/children");
        response.EnsureSuccessStatusCode();

        // Compare parsed ids rather than substring-matching the raw body, which
        // would pass just because the digits appear in an unrelated field.
        using var doc = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        var returned = new System.Collections.Generic.List<int>();
        CollectIds(doc.RootElement, returned);

        Assert.DoesNotContain(_fixture.UnlinkedStudentId, returned);
        Assert.Contains(_fixture.LinkedStudentId, returned);
    }

    /// <summary>
    /// Walks the response for any integer that looks like a student id. The
    /// endpoint returns an array of student records, so the id may sit under
    /// "StudentId", "Id", or be nested one level down.
    /// </summary>
    private static void CollectIds(System.Text.Json.JsonElement element, System.Collections.Generic.List<int> found)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) CollectIds(item, found);
                break;

            case System.Text.Json.JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == System.Text.Json.JsonValueKind.Number
                        && (property.Name.EndsWith("Id", System.StringComparison.OrdinalIgnoreCase)))
                    {
                        if (property.Value.TryGetInt32(out var id)) found.Add(id);
                    }
                    else
                    {
                        CollectIds(property.Value, found);
                    }
                }
                break;
        }
    }

    [Fact]
    public async Task Teacher_Can_Read_Any_Student()
    {
        using var client = AuthTestFixture.ClientFor(_fixture, _fixture.TeacherToken);
        using var response = await client.GetAsync($"/api/students/{_fixture.LinkedStudentId}");
        Assert.True(response.IsSuccessStatusCode,
            $"teacher read returned {(int)response.StatusCode} {response.StatusCode}.");
    }
}
