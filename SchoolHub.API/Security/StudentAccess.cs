using Dapper;
using System.Data;
using System.Security.Claims;

namespace SchoolHub.API.Security;

/// <summary>
/// Decides whether the calling user may read a given student's records.
///
/// Several read endpoints were reachable by any authenticated user, which meant a
/// student token could pull the whole roster and any individual's attendance,
/// results, fees and assignments by changing the id in the URL. The rules here
/// mirror the frontend nav matrix:
///
///   Admin   → any student
///   Teacher → any student
///   Student → only their own record
///   Parent  → only children linked through studentparents
///
/// studentparents.parentid references parents.id (not users.id), so a parent's
/// own user id has to be resolved through the parents table first.
/// </summary>
public static class StudentAccess
{
    /// <summary>The caller's user id from the NameIdentifier claim, or 0.</summary>
    public static int CallerUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : 0;
    }

    /// <summary>Admin or Teacher — roles that legitimately see every student.</summary>
    public static bool IsStaff(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.IsInRole("Teacher");

    /// <summary>
    /// True when the caller may read records for <paramref name="studentId"/>.
    /// </summary>
    public static async Task<bool> CanReadStudentAsync(
        IDbConnection db,
        ClaimsPrincipal user,
        int studentId)
    {
        if (studentId <= 0) return false;
        if (IsStaff(user)) return true;

        var callerId = CallerUserId(user);
        if (callerId == 0) return false;

        // Student: their own record only.
        if (user.IsInRole("Student"))
        {
            var own = await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM Students WHERE Id = @StudentId AND UserId = @UserId",
                new { StudentId = studentId, UserId = callerId });
            return own.HasValue;
        }

        // Parent: only children explicitly linked to them.
        if (user.IsInRole("Parent"))
        {
            var linked = await db.ExecuteScalarAsync<int?>(@"
                SELECT sp.StudentId
                FROM studentparents sp
                JOIN parents p ON p.Id = sp.ParentId
                WHERE sp.StudentId = @StudentId AND p.UserId = @UserId
                LIMIT 1",
                new { StudentId = studentId, UserId = callerId });
            return linked.HasValue;
        }

        return false;
    }
}
