using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace SchoolHub.Tests;

/// <summary>
/// Locks down the role matrix. Every entry was derived from the
/// <c>[Authorize(Roles = ...)]</c> attributes on the controllers, so a change
/// that widens or narrows a role now fails here instead of in the browser.
///
/// Status codes under test:
///   200 / 403 — the authorization decision itself
///   401       — no token supplied
///   500       — reserved for genuine server faults (see ErrorMappingTests)
/// </summary>
[Collection(AuthTestCollection.Name)]
public class AuthorizationMatrixTests
{
    private readonly AuthTestFixture _fixture;

    public AuthorizationMatrixTests(AuthTestFixture fixture) => _fixture = fixture;

    /// <summary>Roles that may call each endpoint, and the path to call.</summary>
    public static IEnumerable<object[]> Matrix => new List<object[]>
    {
        // ---- Admin,Teacher ----
        new object[] { "GET",  "/api/students",        "Admin",   true  },
        new object[] { "GET",  "/api/students",        "Teacher", true  },
        new object[] { "GET",  "/api/students",        "Student", false },
        new object[] { "GET",  "/api/students",        "Parent",  false },

        new object[] { "GET",  "/api/teachers",        "Admin",   true  },
        new object[] { "GET",  "/api/teachers",        "Teacher", true  },
        new object[] { "GET",  "/api/teachers",        "Student", false },
        new object[] { "GET",  "/api/teachers",        "Parent",  false },

        new object[] { "GET",  "/api/academic/classes",   "Admin",   true  },
        new object[] { "GET",  "/api/academic/classes",   "Teacher", true  },
        new object[] { "GET",  "/api/academic/classes",   "Student", false },
        new object[] { "GET",  "/api/academic/classes",   "Parent",  false },

        new object[] { "GET",  "/api/academic/subjects",  "Admin",   true  },
        new object[] { "GET",  "/api/academic/subjects",  "Teacher", true  },
        new object[] { "GET",  "/api/academic/subjects",  "Student", false },

        new object[] { "GET",  "/api/academic/sections",  "Admin",   true  },
        new object[] { "GET",  "/api/academic/sections",  "Teacher", true  },
        new object[] { "GET",  "/api/academic/sections",  "Parent",  false },

        new object[] { "GET",  "/api/teacher/classes",    "Admin",   true  },
        new object[] { "GET",  "/api/teacher/classes",    "Teacher", true  },
        new object[] { "GET",  "/api/teacher/classes",    "Student", false },

        // ---- Admin,Teacher,Student ----
        new object[] { "GET",  "/api/assignments",     "Admin",   true  },
        new object[] { "GET",  "/api/assignments",     "Teacher", true  },
        new object[] { "GET",  "/api/assignments",     "Student", true  },
        new object[] { "GET",  "/api/assignments",     "Parent",  false },

        new object[] { "GET",  "/api/exams",           "Admin",   true  },
        new object[] { "GET",  "/api/exams",           "Teacher", true  },
        new object[] { "GET",  "/api/exams",           "Student", true  },
        new object[] { "GET",  "/api/exams",           "Parent",  false },

        // ---- Admin only ----
        new object[] { "GET",  "/api/admin/dashboard/stats", "Admin",   true  },
        new object[] { "GET",  "/api/admin/dashboard/stats", "Teacher", false },
        new object[] { "GET",  "/api/admin/dashboard/stats", "Student", false },
        new object[] { "GET",  "/api/admin/dashboard/stats", "Parent",  false },

        new object[] { "GET",  "/api/fees/structures", "Admin",   true  },
        new object[] { "GET",  "/api/fees/structures", "Teacher", false },
        new object[] { "GET",  "/api/fees/structures", "Student", false },
        new object[] { "GET",  "/api/fees/structures", "Parent",  false },

        new object[] { "GET",  "/api/auditlogs",       "Admin",   true  },
        new object[] { "GET",  "/api/auditlogs",       "Teacher", false },
        new object[] { "GET",  "/api/auditlogs",       "Student", false },
        new object[] { "GET",  "/api/auditlogs",       "Parent",  false },

        new object[] { "GET",  "/api/reports/students-by-class", "Admin",   true  },
        new object[] { "GET",  "/api/reports/students-by-class", "Teacher", false },
        new object[] { "GET",  "/api/reports/fee-collection",    "Admin",   true  },
        new object[] { "GET",  "/api/reports/fee-collection",    "Student", false },

        // ---- Student only ----
        new object[] { "GET",  "/api/portals/student/dashboard", "Student", true  },
        new object[] { "GET",  "/api/portals/student/dashboard", "Admin",   false },
        new object[] { "GET",  "/api/portals/student/dashboard", "Teacher", false },
        new object[] { "GET",  "/api/portals/student/dashboard", "Parent",  false },

        // ---- Parent only ----
        new object[] { "GET",  "/api/portals/parent/children",  "Parent",  true  },
        new object[] { "GET",  "/api/portals/parent/children",  "Student", false },
        new object[] { "GET",  "/api/portals/parent/children",  "Admin",   false },

        // ---- Any authenticated role ----
        new object[] { "GET",  "/api/profile", "Admin",   true  },
        new object[] { "GET",  "/api/profile", "Teacher", true  },
        new object[] { "GET",  "/api/profile", "Student", true  },
        new object[] { "GET",  "/api/profile", "Parent",  true  },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Endpoint_Allows_Or_Forbids_As_Specified(
        string method, string path, string role, bool allowed)
    {
        using var client = AuthTestFixture.ClientFor(_fixture, TokenFor(role));
        using var response = await client.SendAsync(new HttpRequestMessage(
            new HttpMethod(method), path));

        var expected = allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden;

        Assert.True(
            response.StatusCode == expected,
            $"{method} {path} as {role}: expected {(int)expected} {expected} but got " +
            $"{(int)response.StatusCode} {response.StatusCode}.");
    }

    [Fact]
    public async Task Admin_Only_Endpoints_Return_401_Without_A_Token()
    {
        using var client = _fixture.CreateClient();
        using var response = await client.GetAsync("/api/admin/dashboard/stats");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Garbled_Token_Returns_401_Not_500()
    {
        using var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not.a.real.token");
        using var response = await client.GetAsync("/api/students");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_Route_Returns_404_Not_500()
    {
        using var client = AuthTestFixture.ClientFor(_fixture, _fixture.AdminToken);
        using var response = await client.GetAsync("/api/this-route-does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Profile_Is_Reachable_By_Every_Role()
    {
        // Guards the frontend regression where the avatar button silently
        // redirected to the overview because "profile" was missing from the
        // sidebar item list.
        foreach (var role in new[] { "Admin", "Teacher", "Student", "Parent" })
        {
            using var client = AuthTestFixture.ClientFor(_fixture, TokenFor(role));
            using var response = await client.GetAsync("/api/profile");
            Assert.True(response.IsSuccessStatusCode, $"/api/profile as {role} returned {response.StatusCode}.");
        }
    }

    private string TokenFor(string role) => role switch
    {
        "Admin" => _fixture.AdminToken,
        "Teacher" => _fixture.TeacherToken,
        "Student" => _fixture.StudentToken,
        "Parent" => _fixture.ParentToken,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
    };
}
