using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using SchoolHub.API.Security;
using System.Data;

namespace SchoolHub.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ExamsController : ControllerBase
    {
        /// <summary>
        /// The grade letters, lowest band last. These were hardcoded inline in
        /// the old marks handler; they are exposed so the marking screen can
        /// show a teacher the same bands the server grades against.
        /// </summary>
        public static readonly (string Grade, decimal MinPercentage)[] GradeBands =
        {
            ("A+", 90m),
            ("A", 80m),
            ("B", 70m),
            ("C", 60m),
            ("F", 0m),
        };

        /// <summary>
        /// Fallback when an exam has no PassingMarks. The column defaults to 40,
        /// but a row can still hold null and dividing by it would throw.
        /// </summary>
        private const decimal DefaultPassingPercentage = 40m;

        private readonly string _connectionString;

        public ExamsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        /* ------------------------------ read ------------------------------ */

        /// <summary>
        /// The exam list, with each exam's class coverage and how much of each
        /// paper has been marked.
        ///
        /// Previously this was <c>SELECT *</c> in whatever order the table
        /// happened to return, so the list gave no dates, no year, and no sign of
        /// an exam that had no subjects at all. SubjectCount is joined in because
        /// an exam with no papers is the state a freshly created exam starts in,
        /// and the list gave no way to tell that from a finished one.
        ///
        /// A Parent is excluded, matching the nav matrix: a parent sees their
        /// child's results and nothing else, so the whole school exam list would
        /// be data they have no screen for.
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Admin,Teacher,Student")]
        public async Task<IActionResult> GetExams()
        {
            using var db = Connection;

            var exams = await db.QueryAsync(@"
                SELECT e.Id,
                       e.Title,
                       e.AcademicYearId,
                       e.StartDate,
                       e.EndDate,
                       e.PassingMarks,
                       ay.Name AS AcademicYearName,
                       count(DISTINCT es.ClassId) AS ClassCount,
                       count(DISTINCT es.Id) AS SubjectCount,
                       -- count(), not sum(... IS NOT NULL): PostgreSQL has no
                       -- sum(boolean), so the list threw a 500 for every caller.
                       count(m.Id) AS MarkCount
                FROM Exams e
                LEFT JOIN AcademicYears ay ON e.AcademicYearId = ay.Id
                LEFT JOIN ExamSubjects es ON es.ExamId = e.Id
                LEFT JOIN Marks m ON m.ExamSubjectId = es.Id
                GROUP BY e.Id, e.Title, e.AcademicYearId, e.StartDate, e.EndDate, e.PassingMarks, ay.Name
                ORDER BY e.StartDate DESC NULLS LAST, e.Id DESC");

            return Ok(exams);
        }

        /// <summary>One exam with its papers, for the setup and history screens.</summary>
        [HttpGet("{id}")]
        [Authorize(Roles = "Admin,Teacher,Student")]
        public async Task<IActionResult> GetExam(int id)
        {
            using var db = Connection;

            var exam = await db.QueryFirstOrDefaultAsync(@"
                SELECT e.Id,
                       e.Title,
                       e.AcademicYearId,
                       e.StartDate,
                       e.EndDate,
                       e.PassingMarks,
                       ay.Name AS AcademicYearName
                FROM Exams e
                LEFT JOIN AcademicYears ay ON e.AcademicYearId = ay.Id
                WHERE e.Id = @Id", new { Id = id });
            if (exam == null) return NotFound(new { Message = "That exam does not exist." });

            var subjects = await db.QueryAsync(@"
                SELECT es.Id,
                       es.ExamId,
                       es.ClassId,
                       es.SubjectId,
                       es.MaxMarks,
                       es.ExamDate,
                       c.Name AS ClassName,
                       s.Name AS SubjectName,
                       s.Code AS SubjectCode,
                       count(m.Id) AS MarkCount
                FROM ExamSubjects es
                JOIN Classes c ON es.ClassId = c.Id
                LEFT JOIN Subjects s ON es.SubjectId = s.Id
                LEFT JOIN Marks m ON m.ExamSubjectId = es.Id
                WHERE es.ExamId = @Id
                GROUP BY es.Id, es.ExamId, es.ClassId, es.SubjectId, es.MaxMarks,
                         es.ExamDate, c.Name, s.Name, s.Code
                ORDER BY c.Name, s.Name, es.Id", new { Id = id });

            return Ok(new { Exam = exam, Subjects = subjects });
        }

        /// <summary>
        /// The students to mark, for one paper. Mirrors the attendance roster:
        /// every student enrolled in the paper's class, with whatever is already
        /// recorded attached, so re-marking starts from the stored marks instead
        /// of asking the teacher to retype a full roster.
        /// </summary>
        [HttpGet("subjects/{examSubjectId}/roster")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> GetMarksRoster(int examSubjectId)
        {
            using var db = Connection;

            var paper = await db.QueryFirstOrDefaultAsync<PaperRow>(@"
                SELECT es.Id, es.ExamId, es.ClassId, es.SubjectId, es.MaxMarks, es.ExamDate,
                       c.Name AS ClassName,
                       s.Name AS SubjectName,
                       e.Title AS ExamTitle,
                       e.PassingMarks
                FROM ExamSubjects es
                JOIN Classes c ON es.ClassId = c.Id
                LEFT JOIN Subjects s ON es.SubjectId = s.Id
                JOIN Exams e ON es.ExamId = e.Id
                WHERE es.Id = @Id", new { Id = examSubjectId });
            if (paper == null) return NotFound(new { Message = "That exam subject does not exist." });

            // A class can hold several sections and the paper is class-scoped, so
            // the roster is every enrollment in the class rather than one section.
            var roster = await db.QueryAsync(@"
                SELECT s.Id AS StudentId,
                       s.RollNumber,
                       u.Username,
                       m.Id AS MarkId,
                       m.MarksObtained,
                       m.Grade,
                       m.Remarks
                FROM Enrollments e
                JOIN Students s ON e.StudentId = s.Id
                JOIN Users u ON s.UserId = u.Id
                LEFT JOIN Marks m ON m.StudentId = s.Id AND m.ExamSubjectId = @ExamSubjectId
                WHERE e.ClassId = @ClassId
                ORDER BY s.RollNumber",
                new { ExamSubjectId = examSubjectId, ClassId = paper.ClassId });

            return Ok(new
            {
                ExamSubjectId = paper.Id,
                ExamId = paper.ExamId,
                ExamTitle = paper.ExamTitle,
                ClassId = paper.ClassId,
                ClassName = paper.ClassName,
                SubjectId = paper.SubjectId,
                SubjectName = paper.SubjectName,
                MaxMarks = paper.MaxMarks,
                PassingPercentage = paper.PassingMarks ?? DefaultPassingPercentage,
                ExamDate = paper.ExamDate,
                Students = roster,
            });
        }

        /// <summary>
        /// The caller's own results, for a Student.
        ///
        /// The only results path was /api/students/{id}/results, and nothing
        /// exposed a student's own Students.Id: /api/profile returns the user row
        /// only. Resolving it from the token means the page cannot show an empty
        /// transcript by asking for the wrong student.
        /// </summary>
        [HttpGet("mine")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> GetMyResults()
        {
            using var db = Connection;

            var studentId = await db.ExecuteScalarAsync<int?>(
                "SELECT Id FROM Students WHERE UserId = @UserId",
                new { UserId = StudentAccess.CallerUserId(User) });
            if (!studentId.HasValue)
                return NotFound(new { Message = "This account is not linked to a student record." });

            return Ok(await QueryStudentResultsAsync(db, studentId.Value));
        }

        /// <summary>
        /// One student's results. Admin and Teacher may read any student; a
        /// Student only their own record and a Parent only a linked child, so
        /// editing the id in the URL is not enough to read someone else's.
        /// </summary>
        [HttpGet("student/{studentId}")]
        [Authorize]
        public async Task<IActionResult> GetStudentResults(int studentId)
        {
            using var db = Connection;
            if (!await StudentAccess.CanReadStudentAsync(db, User, studentId)) return Forbid();

            return Ok(await QueryStudentResultsAsync(db, studentId));
        }

        /// <summary>
        /// One student's results, with the paper and class each mark belongs to
        /// and a per-exam total, so the student and parent pages do not have to
        /// pair bare marks against a subject list they were never given.
        ///
        /// The old students/{id}/results returned no exam id, no date and no
        /// class, so a transcript could not be grouped or ordered.
        /// </summary>
        private static async Task<IEnumerable<TranscriptRow>> QueryStudentResultsAsync(
            IDbConnection db,
            int studentId)
        {
            // Typed rather than dynamic: a Select over a List<dynamic> is bound at
            // run time, so the projection threw instead of returning rows.
            var marks = await db.QueryAsync<MarkRow>(@"
                SELECT m.Id,
                       m.MarksObtained,
                       m.Grade,
                       m.Remarks,
                       es.Id AS ExamSubjectId,
                       es.MaxMarks,
                       es.ExamDate,
                       es.ExamId,
                       es.ClassId,
                       c.Name AS ClassName,
                       s.Id AS SubjectId,
                       s.Name AS SubjectName,
                       s.Code AS SubjectCode,
                       e.Title AS ExamTitle,
                       e.PassingMarks
                FROM Marks m
                JOIN ExamSubjects es ON m.ExamSubjectId = es.Id
                LEFT JOIN Classes c ON es.ClassId = c.Id
                LEFT JOIN Subjects s ON es.SubjectId = s.Id
                JOIN Exams e ON es.ExamId = e.Id
                WHERE m.StudentId = @StudentId
                ORDER BY e.StartDate DESC NULLS LAST, e.Id DESC, c.Name, s.Name", new { StudentId = studentId });

            var transcript = new List<ExamTotals>();
            ExamTotals? current = null;

            foreach (var row in marks)
            {
                if (current == null || current.ExamId != row.ExamId)
                {
                    // The pass mark belongs to the exam and differs between exams,
                    // so it is read per exam rather than hoisted out of the loop
                    // and applied to all of them. A null falls back to the default.
                    var passPercentage = row.PassingMarks ?? DefaultPassingPercentage;
                    current = new ExamTotals(row.ExamId, row.ExamTitle, passPercentage);
                    transcript.Add(current);
                }

                var percentage = row.MaxMarks == 0m ? 0m : Math.Round(row.MarksObtained / row.MaxMarks * 100m, 2);
                current.Add(percentage >= current.PassingPercentage, row.MarksObtained, row.MaxMarks);
            }

            // One row per exam carrying its subject marks, so the caller does not
            // have to reimplement the rollup to show a percentage.
            return transcript.Select(e => e.ToTranscriptRow()).ToList();
        }

        /* ----------------------------- writes ----------------------------- */

        /// <summary>
        /// Creates an exam. Titles are unique per academic year: two exams called
        /// "Mid-Term Exam" in one year are indistinguishable in a transcript, and
        /// the two rows already in the seed data are exactly that shape.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> CreateExam([FromBody] SaveExamDto dto)
        {
            var problem = ValidateExam(dto, requireAcademicYear: true);
            if (problem != null) return problem;

            using var db = Connection;

            if (await ExamTitleTakenAsync(db, dto.Title!, dto.AcademicYearId))
            {
                return Conflict(new
                {
                    Message = $"An exam called \"{dto.Title}\" already exists for that academic year.",
                });
            }

            var id = await db.ExecuteScalarAsync<int>(@"
                INSERT INTO Exams (Title, AcademicYearId, StartDate, EndDate, PassingMarks)
                VALUES (@Title, @AcademicYearId, @StartDate, @EndDate, @PassingMarks)
                RETURNING Id",
                new
                {
                    Title = dto.Title!.Trim(),
                    dto.AcademicYearId,
                    StartDate = dto.StartDate?.Date,
                    EndDate = dto.EndDate?.Date,
                    PassingMarks = dto.PassingMarks ?? DefaultPassingPercentage,
                });

            return Ok(new { Message = "Exam created successfully", ExamId = id });
        }

        /// <summary>
        /// Updates an exam's own fields. Papers are left alone: renaming the exam
        /// does not change which papers it holds, and the papers have their own
        /// guarded delete.
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> UpdateExam(int id, [FromBody] SaveExamDto dto)
        {
            var problem = ValidateExam(dto, requireAcademicYear: true);
            if (problem != null) return problem;

            using var db = Connection;

            if (!(await db.QueryFirstOrDefaultAsync<int?>("SELECT Id FROM Exams WHERE Id = @Id", new { Id = id })).HasValue)
                return NotFound(new { Message = "That exam does not exist." });

            if (await ExamTitleTakenAsync(db, dto.Title!, dto.AcademicYearId, exceptId: id))
            {
                return Conflict(new
                {
                    Message = $"An exam called \"{dto.Title}\" already exists for that academic year.",
                });
            }

            await db.ExecuteAsync(@"
                UPDATE Exams
                SET Title = @Title,
                    AcademicYearId = @AcademicYearId,
                    StartDate = @StartDate,
                    EndDate = @EndDate,
                    PassingMarks = @PassingMarks
                WHERE Id = @Id",
                new
                {
                    Title = dto.Title!.Trim(),
                    dto.AcademicYearId,
                    StartDate = dto.StartDate?.Date,
                    EndDate = dto.EndDate?.Date,
                    PassingMarks = dto.PassingMarks ?? DefaultPassingPercentage,
                    Id = id,
                });

            return Ok(new { Message = "Exam updated successfully", ExamId = id });
        }

        /// <summary>
        /// Deletes an exam, but only when it holds no papers. Papers cascade their
        /// marks, so a blanket delete would quietly discard results.
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteExam(int id)
        {
            using var db = Connection;

            var title = await db.QueryFirstOrDefaultAsync<string>(
                "SELECT Title FROM Exams WHERE Id = @Id", new { Id = id });
            if (title == null) return NotFound(new { Message = "That exam does not exist." });

            var subjects = await db.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM ExamSubjects WHERE ExamId = @Id", new { Id = id });
            if (subjects > 0)
            {
                return Conflict(new
                {
                    Message = $"\"{title}\" still has {subjects} subject(s). Remove them first.",
                });
            }

            await db.ExecuteAsync("DELETE FROM Exams WHERE Id = @Id", new { Id = id });
            return Ok(new { Message = $"\"{title}\" deleted." });
        }

        /// <summary>
        /// Adds a paper to an exam: one subject, for one class, out of one.
        ///
        /// This write path did not exist at all. Without it the table could only
        /// ever be empty, so EnterMarks had nothing to read a MaxMarks from and
        /// the entire marking flow was unreachable.
        /// </summary>
        [HttpPost("{id}/subjects")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> AddExamSubject(int id, [FromBody] SaveExamSubjectDto dto)
        {
            using var db = Connection;

            if (!(await db.QueryFirstOrDefaultAsync<int?>("SELECT Id FROM Exams WHERE Id = @Id", new { Id = id })).HasValue)
                return NotFound(new { Message = "That exam does not exist." });

            var problem = await ValidateSubjectAsync(db, dto);
            if (problem != null) return problem;

            // The unique index covers this too, but catching it here names the
            // clash instead of surfacing a bare 409.
            if ((await db.ExecuteScalarAsync<int?>(@"
                SELECT Id FROM ExamSubjects
                WHERE ExamId = @ExamId AND ClassId = @ClassId AND SubjectId = @SubjectId",
                new { ExamId = id, dto.ClassId, dto.SubjectId })).HasValue)
            {
                return Conflict(new { Message = "That exam already has this subject for this class." });
            }

            var subjectId = await db.ExecuteScalarAsync<int>(@"
                INSERT INTO ExamSubjects (ExamId, ClassId, SubjectId, MaxMarks, ExamDate)
                VALUES (@ExamId, @ClassId, @SubjectId, @MaxMarks, @ExamDate)
                RETURNING Id",
                new
                {
                    ExamId = id,
                    dto.ClassId,
                    dto.SubjectId,
                    dto.MaxMarks,
                    ExamDate = dto.ExamDate?.ToUniversalTime(),
                });

            return Ok(new { Message = "Subject added to exam", ExamSubjectId = subjectId });
        }

        /// <summary>
        /// Removes a paper, but only when nothing has been marked against it. The
        /// marks cascade from the paper, so an empty check has to happen first.
        /// </summary>
        [HttpDelete("{examId}/subjects/{examSubjectId}")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> DeleteExamSubject(int examId, int examSubjectId)
        {
            using var db = Connection;

            var paper = await db.QueryFirstOrDefaultAsync<(string SubjectName, string ClassName)?>(@"
                SELECT s.Name AS SubjectName, c.Name AS ClassName
                FROM ExamSubjects es
                LEFT JOIN Subjects s ON es.SubjectId = s.Id
                JOIN Classes c ON es.ClassId = c.Id
                WHERE es.Id = @Id AND es.ExamId = @ExamId",
                new { Id = examSubjectId, ExamId = examId });
            if (paper == null) return NotFound(new { Message = "That exam subject does not exist." });

            var marks = await db.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM Marks WHERE ExamSubjectId = @Id", new { Id = examSubjectId });
            if (marks > 0)
            {
                return Conflict(new
                {
                    Message = $"{paper.Value.SubjectName} for {paper.Value.ClassName} has {marks} mark(s). Remove the marks first.",
                });
            }

            await db.ExecuteAsync("DELETE FROM ExamSubjects WHERE Id = @Id", new { Id = examSubjectId });
            return Ok(new { Message = $"{paper.Value.SubjectName} for {paper.Value.ClassName} removed." });
        }

        /// <summary>
        /// Records a paper's whole roster in one transaction.
        ///
        /// The old handler took one student per call, so marking a class of thirty
        /// meant thirty saves and a teacher who stopped halfway left a paper that
        /// looked complete in the list. One request is now all-or-nothing.
        ///
        /// Marks are matched by student id and updated in place, so re-saving
        /// cannot duplicate a row, and the unique index on (ExamSubjectId,
        /// StudentId) makes a retry safe. A remark comes from the payload, so the
        /// marking screen pre-fills it from the roster rather than dropping it.
        /// </summary>
        [HttpPut("subjects/{examSubjectId}/marks")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> SaveMarks(int examSubjectId, [FromBody] SaveMarksDto dto)
        {
            if (dto.Marks.Count == 0)
                return BadRequest(new { Message = "Mark at least one student." });

            using var db = Connection;
            db.Open();
            using var transaction = db.BeginTransaction();

            try
            {
                var paper = await db.QueryFirstOrDefaultAsync<PaperRow>(@"
                    SELECT es.Id, es.ExamId, es.ClassId, es.SubjectId, es.MaxMarks, es.ExamDate,
                           e.Title AS ExamTitle, e.PassingMarks
                    FROM ExamSubjects es
                    JOIN Exams e ON es.ExamId = e.Id
                    WHERE es.Id = @Id", new { Id = examSubjectId }, transaction);
                if (paper == null)
                    return NotFound(new { Message = "That exam subject does not exist." });

                // The same student twice would otherwise be saved twice, with the
                // last one silently winning.
                var seen = new HashSet<int>();
                foreach (var item in dto.Marks)
                {
                    if (!seen.Add(item.StudentId))
                        return BadRequest(new { Message = $"Student {item.StudentId} appears more than once." });
                }

                // A mark outside 0..MaxMarks is not a typo to be rounded off: it
                // is the only thing standing between a result and a percentage
                // above 100, or a negative mark on a transcript.
                var outOfRange = dto.Marks
                    .Where(m => m.MarksObtained < 0m || m.MarksObtained > paper.MaxMarks)
                    .Select(m => m.StudentId)
                    .ToArray();
                if (outOfRange.Length > 0)
                {
                    return BadRequest(new
                    {
                        Message = $"Mark(s) for student id(s) {string.Join(", ", outOfRange)} are outside 0 to {paper.MaxMarks}.",
                    });
                }

                // Only students actually enrolled in this paper's class may be
                // marked, otherwise a roster from another grade is filed under
                // this one.
                var ids = dto.Marks.Select(m => m.StudentId).ToArray();
                var enrolled = (await db.QueryAsync<int>(
                    "SELECT StudentId FROM Enrollments WHERE ClassId = @ClassId AND StudentId = ANY(@Ids)",
                    new { ClassId = paper.ClassId, Ids = ids },
                    transaction)).ToHashSet();
                var strays = ids.Where(x => !enrolled.Contains(x)).ToArray();
                if (strays.Length > 0)
                {
                    return BadRequest(new
                    {
                        Message = $"Student id(s) {string.Join(", ", strays)} are not enrolled in {paper.ExamTitle}.",
                    });
                }

                var passPercentage = paper.PassingMarks ?? DefaultPassingPercentage;
                var saved = new List<MarkResult>();

                foreach (var item in dto.Marks)
                {
                    var percentage = Math.Round(item.MarksObtained / paper.MaxMarks * 100m, 2);
                    var grade = GradeFor(percentage);
                    var remarks = item.Remarks ?? string.Empty;

                    await db.ExecuteAsync(@"
                        INSERT INTO Marks (ExamSubjectId, StudentId, MarksObtained, Grade, Remarks)
                        VALUES (@ExamSubjectId, @StudentId, @MarksObtained, @Grade, @Remarks)
                        ON CONFLICT (ExamSubjectId, StudentId) DO UPDATE
                        SET MarksObtained = EXCLUDED.MarksObtained,
                            Grade = EXCLUDED.Grade,
                            Remarks = EXCLUDED.Remarks",
                        new
                        {
                            ExamSubjectId = examSubjectId,
                            StudentId = item.StudentId,
                            MarksObtained = item.MarksObtained,
                            Grade = grade,
                            Remarks = remarks,
                        },
                        transaction);

                    saved.Add(new MarkResult
                    {
                        StudentId = item.StudentId,
                        MarksObtained = item.MarksObtained,
                        MaxMarks = paper.MaxMarks,
                        Percentage = percentage,
                        Grade = grade,
                        Passed = percentage >= passPercentage,
                    });
                }

                await db.ExecuteAsync(@"
                    INSERT INTO AuditLogs (UserId, Action, Details)
                    VALUES (@UserId, 'SAVE_MARKS', @Details)",
                    new
                    {
                        UserId = StudentAccess.CallerUserId(User),
                        Details = $"Saved marks for exam {paper.ExamId} subject {examSubjectId} ({saved.Count} student(s))",
                    },
                    transaction);

                transaction.Commit();
                return Ok(new
                {
                    Message = "Marks saved",
                    ExamSubjectId = examSubjectId,
                    Saved = saved.Count,
                    Marks = saved,
                });
            }
            catch
            {
                // The transaction rolls back on dispose; the rethrow lets
                // ExceptionMiddleware log and classify it.
                throw;
            }
        }

        /* ----------------------------- helpers ----------------------------- */

        /// <summary>The grade for a percentage, using the bands above.</summary>
        public static string GradeFor(decimal percentage)
            => GradeBands.First(b => percentage >= b.MinPercentage).Grade;

        private static ActionResult? ValidateExam(SaveExamDto dto, bool requireAcademicYear)
        {
            var title = (dto.Title ?? string.Empty).Trim();
            if (title.Length == 0) return new BadRequestObjectResult(new { Message = "Exam title is required." });
            if (title.Length > 255) return new BadRequestObjectResult(new { Message = "Exam title is too long." });

            if (requireAcademicYear && dto.AcademicYearId <= 0)
                return new BadRequestObjectResult(new { Message = "An academic year is required." });

            // The window is compared here as well as by the check constraint, so
            // the caller is told which field is wrong instead of getting a
            // generic constraint failure.
            if (dto.StartDate.HasValue && dto.EndDate.HasValue && dto.EndDate.Value.Date < dto.StartDate.Value.Date)
            {
                return new BadRequestObjectResult(new { Message = "The end date cannot be before the start date." });
            }

            if (dto.PassingMarks.HasValue && (dto.PassingMarks < 0m || dto.PassingMarks > 100m))
            {
                return new BadRequestObjectResult(new { Message = "The pass mark must be a percentage between 0 and 100." });
            }

            return null;
        }

        private static async Task<ActionResult?> ValidateSubjectAsync(IDbConnection db, SaveExamSubjectDto dto)
        {
            if (dto.ClassId <= 0) return new BadRequestObjectResult(new { Message = "A class is required." });
            if (dto.SubjectId <= 0) return new BadRequestObjectResult(new { Message = "A subject is required." });

            // Zero here divided to the grade percentage, so the whole save threw
            // a divide by zero rather than rejecting the input.
            if (dto.MaxMarks <= 0m)
                return new BadRequestObjectResult(new { Message = "Maximum marks must be greater than zero." });

            if (!(await db.QueryFirstOrDefaultAsync<int?>("SELECT Id FROM Classes WHERE Id = @Id", new { Id = dto.ClassId })).HasValue)
                return new NotFoundObjectResult(new { Message = "That class does not exist." });

            if (!(await db.QueryFirstOrDefaultAsync<int?>("SELECT Id FROM Subjects WHERE Id = @Id", new { Id = dto.SubjectId })).HasValue)
                return new NotFoundObjectResult(new { Message = "That subject does not exist." });

            return null;
        }

        private static async Task<bool> ExamTitleTakenAsync(
            IDbConnection db,
            string title,
            int academicYearId,
            int? exceptId = null)
        {
            // Academic years are not names here: the seed data has two rows both
            // called "2024-2025", so comparing on Name would collide them.
            return (await db.QueryFirstOrDefaultAsync<int?>(
                "SELECT Id FROM Exams WHERE lower(Title) = lower(@Title) AND AcademicYearId = @AcademicYearId AND (@ExceptId IS NULL OR Id <> @ExceptId)",
                new { Title = title, AcademicYearId = academicYearId, ExceptId = exceptId })).HasValue;
        }

        private class PaperRow
        {
            public int Id { get; set; }
            public int ExamId { get; set; }
            public int ClassId { get; set; }
            public int? SubjectId { get; set; }
            public decimal MaxMarks { get; set; }
            public DateTime? ExamDate { get; set; }
            public string? ExamTitle { get; set; }
            public string? ClassName { get; set; }
            public string? SubjectName { get; set; }
            public decimal? PassingMarks { get; set; }
        }

        /// <summary>One mark, as read for the transcript rollup.</summary>
        private class MarkRow
        {
            public int Id { get; set; }
            public decimal MarksObtained { get; set; }
            public string? Grade { get; set; }
            public string? Remarks { get; set; }
            public int ExamSubjectId { get; set; }
            public decimal MaxMarks { get; set; }
            public DateTime? ExamDate { get; set; }
            public int ExamId { get; set; }
            public int ClassId { get; set; }
            public string? ClassName { get; set; }
            public int? SubjectId { get; set; }
            public string? SubjectName { get; set; }
            public string? SubjectCode { get; set; }
            public string? ExamTitle { get; set; }
            public decimal? PassingMarks { get; set; }
        }

        /// <summary>
        /// Running totals for one exam. Kept separate from
        /// <see cref="TranscriptRow"/> rather than inheriting from it: the
        /// accumulator holds private fields behind computed properties, and
        /// reading those through the base type returned the unset fields instead.
        /// </summary>
        private class ExamTotals
        {
            private decimal _obtained;
            private decimal _max;

            public ExamTotals(int examId, string? title, decimal passingPercentage)
            {
                ExamId = examId;
                ExamTitle = title;
                PassingPercentage = passingPercentage;
            }

            public int ExamId { get; }
            public string? ExamTitle { get; }
            public decimal PassingPercentage { get; }
            public int SubjectCount { get; private set; }
            public int PassedCount { get; private set; }

            public void Add(bool passed, decimal obtained, decimal maxMarks)
            {
                SubjectCount++;
                if (passed) PassedCount++;
                _obtained += obtained;
                _max += maxMarks;
            }

            public TranscriptRow ToTranscriptRow()
            {
                var percentage = _max == 0m ? 0m : Math.Round(_obtained / _max * 100m, 2);
                return new TranscriptRow
                {
                    ExamId = ExamId,
                    ExamTitle = ExamTitle,
                    PassingPercentage = PassingPercentage,
                    SubjectCount = SubjectCount,
                    PassedCount = PassedCount,
                    TotalObtained = _obtained,
                    TotalMax = _max,
                    OverallPercentage = percentage,
                    Passed = percentage >= PassingPercentage,
                };
            }
        }
    }

    public class SaveExamDto
    {
        public string Title { get; set; } = string.Empty;
        public int AcademicYearId { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        /// <summary>Pass threshold as a percentage. Defaults to 40 server-side.</summary>
        public decimal? PassingMarks { get; set; }
    }

    public class SaveExamSubjectDto
    {
        public int ClassId { get; set; }
        public int SubjectId { get; set; }
        public decimal MaxMarks { get; set; }
        public DateTime? ExamDate { get; set; }
    }

    public class SaveMarksDto
    {
        public List<MarkItemDto> Marks { get; set; } = new();
    }

    public class MarkItemDto
    {
        public int StudentId { get; set; }
        public decimal MarksObtained { get; set; }
        public string Remarks { get; set; } = string.Empty;
    }

    /// <summary>What the server graded, echoed back so the screen can show it.</summary>
    public class MarkResult
    {
        public int StudentId { get; set; }
        public decimal MarksObtained { get; set; }
        public decimal MaxMarks { get; set; }
        public decimal Percentage { get; set; }
        public string Grade { get; set; } = string.Empty;
        public bool Passed { get; set; }
    }

    /// <summary>One exam's result for one student, rolled up across its papers.</summary>
    public class TranscriptRow
    {
        public int ExamId { get; set; }
        public string? ExamTitle { get; set; }
        public decimal PassingPercentage { get; set; }
        public int SubjectCount { get; set; }
        public int PassedCount { get; set; }
        public decimal TotalObtained { get; set; }
        public decimal TotalMax { get; set; }
        public decimal OverallPercentage { get; set; }
        public bool Passed { get; set; }
    }
}
