using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;

namespace SchoolHub.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AcademicController : ControllerBase
    {
        private readonly string _connectionString;

        public AcademicController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        /// <summary>Academic setup data, used by the staff-facing screens.</summary>
        [HttpGet("classes")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> GetClasses()
        {
            using var db = Connection;
            // SectionCount lets the admin see at a glance which classes are still
            // unconfigured, without a second round trip per row.
            var classes = await db.QueryAsync(@"
                SELECT c.Id, c.Name, count(s.Id) AS SectionCount
                FROM Classes c
                LEFT JOIN Sections s ON s.ClassId = c.Id
                GROUP BY c.Id, c.Name
                ORDER BY c.Name, c.Id");
            return Ok(classes);
        }

        [HttpPost("classes")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateClass([FromBody] CreateClassDto dto)
        {
            var name = (dto.Name ?? string.Empty).Trim();
            if (name.Length == 0) return BadRequest(new { Message = "Class name is required." });

            using var db = Connection;
            // Class names are unique (ux_classes_name). Reporting it as a 400 here
            // gives a better message than the middleware's generic 409, and the
            // insert is the only place the collision can be detected cheaply.
            if (await ClassNameTakenAsync(db, name)) return BadRequest(new { Message = $"A class named \"{name}\" already exists." });

            var sql = "INSERT INTO Classes (Name) VALUES (@Name) RETURNING Id;";
            var id = await db.ExecuteScalarAsync<int>(sql, new { Name = name });
            return Ok(new { Message = "Class created successfully", ClassId = id });
        }

        [HttpPut("classes/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateClass(int id, [FromBody] CreateClassDto dto)
        {
            var name = (dto.Name ?? string.Empty).Trim();
            if (name.Length == 0) return BadRequest(new { Message = "Class name is required." });

            using var db = Connection;
            if (!(await db.QueryFirstOrDefaultAsync<int?>("SELECT Id FROM Classes WHERE Id = @Id", new { Id = id })).HasValue)
                return NotFound();

            if (await ClassNameTakenAsync(db, name, exceptId: id))
                return BadRequest(new { Message = $"A class named \"{name}\" already exists." });

            await db.ExecuteAsync("UPDATE Classes SET Name = @Name WHERE Id = @Id", new { Name = name, Id = id });
            return Ok(new { Message = "Class updated successfully" });
        }

        /// <summary>
        /// Deletes a class, but only when nothing depends on it. The FK on
        /// Enrollments.ClassId cascades, which would silently strip every
        /// student in the class of their class assignment. Refusing is safer
        /// than a destructive button.
        /// </summary>
        [HttpDelete("classes/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteClass(int id)
        {
            using var db = Connection;

            var name = await db.QueryFirstOrDefaultAsync<string>("SELECT Name FROM Classes WHERE Id = @Id", new { Id = id });
            if (name == null) return NotFound();

            var enrollments = await db.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM Enrollments WHERE ClassId = @Id", new { Id = id });
            if (enrollments > 0)
            {
                return Conflict(new
                {
                    Message = $"\"{name}\" has {enrollments} student(s) assigned. Move them to another class first.",
                });
            }

            var subjectLinks = await db.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM ClassSubjects WHERE ClassId = @Id", new { Id = id });

            await db.ExecuteAsync("DELETE FROM Classes WHERE Id = @Id", new { Id = id });
            return Ok(new { Message = $"\"{name}\" deleted. {subjectLinks} subject link(s) removed." });
        }

        [HttpGet("sections")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> GetSections()
        {
            using var db = Connection;
            var sections = await db.QueryAsync(@"
                SELECT s.Id, s.Name, s.ClassId, c.Name as ClassName
                FROM Sections s
                JOIN Classes c ON s.ClassId = c.Id
                ORDER BY c.Name, s.Name, s.Id");
            return Ok(sections);
        }

        [HttpPost("sections")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateSection([FromBody] CreateSectionDto dto)
        {
            var name = (dto.Name ?? string.Empty).Trim();
            if (name.Length == 0) return BadRequest(new { Message = "Section name is required." });
            if (dto.ClassId <= 0) return BadRequest(new { Message = "A class is required." });

            using var db = Connection;
            // The parameter must be named Id to match @Id. Passing ClassId
            // leaves @Id unbound, so Dapper throws and the missing class
            // surfaces as a 400 instead of a 404.
            if (!(await db.QueryFirstOrDefaultAsync<int?>("SELECT Id FROM Classes WHERE Id = @Id", new { Id = dto.ClassId })).HasValue)
                return NotFound(new { Message = "That class does not exist." });

            // Section names are unique per class, not globally: "A" is a valid
            // section in every grade.
            if (await SectionNameTakenAsync(db, name, dto.ClassId))
                return BadRequest(new { Message = $"That class already has a section named \"{name}\"." });

            var sql = "INSERT INTO Sections (Name, ClassId) VALUES (@Name, @ClassId) RETURNING Id;";
            var id = await db.ExecuteScalarAsync<int>(sql, new { Name = name, dto.ClassId });
            return Ok(new { Message = "Section created successfully", SectionId = id });
        }

        [HttpPut("sections/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateSection(int id, [FromBody] CreateSectionDto dto)
        {
            var name = (dto.Name ?? string.Empty).Trim();
            if (name.Length == 0) return BadRequest(new { Message = "Section name is required." });
            if (dto.ClassId <= 0) return BadRequest(new { Message = "A class is required." });

            using var db = Connection;

            var existing = await db.QueryFirstOrDefaultAsync<ClassRow>("SELECT ClassId FROM Sections WHERE Id = @Id", new { Id = id });
            if (existing == null) return NotFound();

            if (!(await db.QueryFirstOrDefaultAsync<int?>("SELECT Id FROM Classes WHERE Id = @Id", new { Id = dto.ClassId })).HasValue)
                return NotFound(new { Message = "That class does not exist." });

            if (await SectionNameTakenAsync(db, name, dto.ClassId, exceptId: id))
                return BadRequest(new { Message = $"That class already has a section named \"{name}\"." });

            // Moving a section between classes can strand the students in it:
            // Enrollments carry both ClassId and SectionId, and a section that
            // no longer belongs to the class leaves them mismatched.
            if (existing.ClassId != dto.ClassId)
            {
                var affected = await db.ExecuteScalarAsync<int>(
                    "SELECT count(*) FROM Enrollments WHERE SectionId = @Id", new { Id = id });
                if (affected > 0)
                {
                    return Conflict(new
                    {
                        Message = $"This section has {affected} student(s). Move them to another section before changing the class.",
                    });
                }
            }

            await db.ExecuteAsync(
                "UPDATE Sections SET Name = @Name, ClassId = @ClassId WHERE Id = @Id",
                new { Name = name, dto.ClassId, Id = id });
            return Ok(new { Message = "Section updated successfully" });
        }

        [HttpDelete("sections/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteSection(int id)
        {
            using var db = Connection;

            var row = await db.QueryFirstOrDefaultAsync<SectionRow>(
                "SELECT Name, ClassId FROM Sections WHERE Id = @Id", new { Id = id });
            if (row == null) return NotFound();

            // Enrollments.SectionId has no ON DELETE CASCADE, so the database would
            // reject this with a FK violation. Catch it first with a clear
            // message: a section with students in it is not an empty label.
            var enrollments = await db.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM Enrollments WHERE SectionId = @Id", new { Id = id });
            if (enrollments > 0)
            {
                return Conflict(new
                {
                    Message = $"Section \"{row.Name}\" has {enrollments} student(s). Move them to another section first.",
                });
            }

            await db.ExecuteAsync("DELETE FROM Sections WHERE Id = @Id", new { Id = id });
            return Ok(new { Message = $"Section \"{row.Name}\" deleted." });
        }

        [HttpGet("subjects")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> GetSubjects()
        {
            using var db = Connection;
            // TeacherName is joined in so the teacher module can show who owns a
            // subject without a second lookup per row.
            // Subjects.TeacherId stores a Teachers.Id (that is what
            // AdminManagementController's assign-subjects writes), and the
            // username lives on Users, so the join runs through Teachers.
            var subjects = await db.QueryAsync(@"
                SELECT s.Id, s.Name, s.Code, s.TeacherId, u.Username AS TeacherName
                FROM Subjects s
                LEFT JOIN Teachers t ON t.Id = s.TeacherId
                LEFT JOIN Users u ON u.Id = t.UserId
                ORDER BY s.Name, s.Id");
            return Ok(subjects);
        }

        [HttpPost("subjects")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateSubject([FromBody] CreateSubjectDto dto)
        {
            var name = (dto.Name ?? string.Empty).Trim();
            var code = (dto.Code ?? string.Empty).Trim().ToUpperInvariant();
            if (name.Length == 0) return BadRequest(new { Message = "Subject name is required." });
            if (code.Length == 0) return BadRequest(new { Message = "Subject code is required." });

            using var db = Connection;
            // Subjects.Code is UNIQUE, so a duplicate would otherwise surface as
            // a bare 409 from the middleware.
            if ((await db.QueryFirstOrDefaultAsync<int?>("SELECT Id FROM Subjects WHERE upper(Code) = @Code", new { Code = code })).HasValue)
                return BadRequest(new { Message = $"Subject code \"{code}\" is already in use." });

            var sql = "INSERT INTO Subjects (Name, Code) VALUES (@Name, @Code) RETURNING Id;";
            var id = await db.ExecuteScalarAsync<int>(sql, new { Name = name, Code = code });
            return Ok(new { Message = "Subject created successfully", SubjectId = id });
        }

        [HttpPut("subjects/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateSubject(int id, [FromBody] CreateSubjectDto dto)
        {
            var name = (dto.Name ?? string.Empty).Trim();
            var code = (dto.Code ?? string.Empty).Trim().ToUpperInvariant();
            if (name.Length == 0) return BadRequest(new { Message = "Subject name is required." });
            if (code.Length == 0) return BadRequest(new { Message = "Subject code is required." });

            using var db = Connection;
            if (!(await db.QueryFirstOrDefaultAsync<int?>("SELECT Id FROM Subjects WHERE Id = @Id", new { Id = id })).HasValue)
                return NotFound();

            if ((await db.QueryFirstOrDefaultAsync<int?>(
                    "SELECT Id FROM Subjects WHERE upper(Code) = @Code AND Id <> @Id", new { Code = code, Id = id })).HasValue)
                return BadRequest(new { Message = $"Subject code \"{code}\" is already in use." });

            await db.ExecuteAsync(
                "UPDATE Subjects SET Name = @Name, Code = @Code WHERE Id = @Id",
                new { Name = name, Code = code, Id = id });
            return Ok(new { Message = "Subject updated successfully" });
        }

        /// <summary>
        /// Deletes a subject only when nothing references it. Unlike a class, the
        /// dependent tables here (assignments, exams, marks, timetable) have
        /// varying FK rules, and letting the database decide produced a bare FK
        /// error the admin could not act on.
        /// </summary>
        [HttpDelete("subjects/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteSubject(int id)
        {
            using var db = Connection;

            var code = await db.QueryFirstOrDefaultAsync<string>("SELECT Code FROM Subjects WHERE Id = @Id", new { Id = id });
            if (code == null) return NotFound();

            var blockers = new List<string>();
            foreach (var (table, column, label) in ReferenceTables)
            {
                // table/label come from a hard-coded list, not user input.
                var count = await db.ExecuteScalarAsync<int>(
                    $"SELECT count(*) FROM {table} WHERE {column} = @Id", new { Id = id });
                if (count > 0) blockers.Add($"{count} {label}");
            }

            if (blockers.Count > 0)
            {
                return Conflict(new
                {
                    Message = $"Subject \"{code}\" is still used by {string.Join(", ", blockers)}. Remove those first.",
                });
            }

            await db.ExecuteAsync("DELETE FROM Subjects WHERE Id = @Id", new { Id = id });
            return Ok(new { Message = $"Subject \"{code}\" deleted." });
        }

        // --- CLASS ↔ SUBJECT MAPPING ---

        /// <summary>Subjects offered in a class. Readable by staff; writes are admin-only.</summary>
        [HttpGet("classes/{id}/subjects")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> GetClassSubjects(int id)
        {
            using var db = Connection;
            if (!(await db.QueryFirstOrDefaultAsync<int?>("SELECT Id FROM Classes WHERE Id = @Id", new { Id = id })).HasValue)
                return NotFound();

            var subjects = await db.QueryAsync(@"
                SELECT s.Id, s.Name, s.Code, s.TeacherId
                FROM ClassSubjects cs
                JOIN Subjects s ON s.Id = cs.SubjectId
                WHERE cs.ClassId = @Id
                ORDER BY s.Name, s.Id", new { Id = id });
            return Ok(subjects);
        }

        /// <summary>
        /// Replaces the class's subject list wholesale. The POST-and-collect-id
        /// version needed two round trips; one atomic statement keeps the list
        /// consistent even if the request is retried.
        /// </summary>
        [HttpPut("classes/{id}/subjects")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> SetClassSubjects(int id, [FromBody] AssignClassSubjectsDto dto)
        {
            var ids = (dto.SubjectIdList ?? new List<int>()).Distinct().Where(x => x > 0).ToList();

            using var db = Connection;
            db.Open();
            using var transaction = db.BeginTransaction();

            if (!(await db.QueryFirstOrDefaultAsync<int?>("SELECT Id FROM Classes WHERE Id = @Id", new { Id = id }, transaction)).HasValue)
                return NotFound();

            var missing = ids.Count;
            if (ids.Count > 0)
            {
                // Verify every id up front so a typo returns 400 naming the bad
                // one, instead of aborting halfway through on an FK violation.
                var found = await db.QueryAsync<int>(
                    "SELECT Id FROM Subjects WHERE Id = ANY(@Ids)", new { Ids = ids.ToArray() }, transaction);
                var foundSet = found.ToHashSet();
                var unknown = ids.Where(x => !foundSet.Contains(x)).ToList();
                if (unknown.Count > 0)
                {
                    return BadRequest(new { Message = $"Unknown subject id(s): {string.Join(", ", unknown)}." });
                }
            }

            await db.ExecuteAsync("DELETE FROM ClassSubjects WHERE ClassId = @Id", new { Id = id }, transaction);
            if (ids.Count > 0)
            {
                await db.ExecuteAsync(
                    "INSERT INTO ClassSubjects (ClassId, SubjectId) SELECT @Id, unnest(@Ids)",
                    new { Id = id, Ids = ids.ToArray() }, transaction);
            }

            transaction.Commit();
            return Ok(new { Message = $"Subjects updated ({ids.Count} assigned).", Assigned = ids.Count });
        }

        // --- helpers ---

        private static async Task<bool> ClassNameTakenAsync(IDbConnection db, string name, int? exceptId = null)
            => (await db.QueryFirstOrDefaultAsync<int?>(
                "SELECT Id FROM Classes WHERE lower(Name) = lower(@Name) AND (@ExceptId IS NULL OR Id <> @ExceptId)",
                new { Name = name, ExceptId = exceptId })).HasValue;

        private static async Task<bool> SectionNameTakenAsync(IDbConnection db, string name, int classId, int? exceptId = null)
            => (await db.QueryFirstOrDefaultAsync<int?>(
                "SELECT Id FROM Sections WHERE ClassId = @ClassId AND lower(Name) = lower(@Name) AND (@ExceptId IS NULL OR Id <> @ExceptId)",
                new { Name = name, ClassId = classId, ExceptId = exceptId })).HasValue;

        /// <summary>
        /// Tables that reference a subject, checked before delete so the admin
        /// gets a count instead of a foreign key error. Only names present in
        /// DbInitializer's schema are listed.
        /// </summary>
        private static readonly (string Table, string Column, string Label)[] ReferenceTables =
        {
            ("ClassSubjects", "SubjectId", "class subject mapping(s)"),
            ("Assignments", "SubjectId", "assignment(s)"),
            ("ExamSubjects", "SubjectId", "exam(s)"),
            ("TimetableEntries", "SubjectId", "timetable slot(s)"),
        };

        private class ClassRow
        {
            public int ClassId { get; set; }
        }

        private class SectionRow
        {
            public string? Name { get; set; }
            public int ClassId { get; set; }
        }
    }

    public class CreateClassDto
    {
        public string Name { get; set; } = string.Empty;
    }

    public class CreateSectionDto
    {
        public string Name { get; set; } = string.Empty;
        public int ClassId { get; set; }
    }

    public class CreateSubjectDto
    {
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public class AssignClassSubjectsDto
    {
        public List<int> SubjectIdList { get; set; } = new();
    }
}