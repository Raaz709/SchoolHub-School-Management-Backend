# SchoolHub Backend

Production-quality School Management System API built with C# / ASP.NET Core Web API, Dapper, and PostgreSQL.

## Status: ✅ Backend Complete

All milestones implemented with full CRUD, authentication, role-based access, and comprehensive feature coverage.

The API is covered by an xUnit integration suite (`SchoolHub.Tests`) that runs the real endpoints against a live PostgreSQL database. Current state: **235 passing tests**.

## Features & Module Coverage

1. **Authentication & Accounts**: Login, Register, JWT Access Tokens, Secure Password Hashing (BCrypt), Roles & Permissions, Profile Management & Picture (`/api/auth`, `/api/auth/password`, `/api/profile`).
2. **Admin Dashboard & Management**: Stats, Students, Teachers, Parents CRUD, Search, Filter, Assign Class/Section/Subjects (`/api/admin/*`).
3. **Academic Management**: Academic Years, plus Admin CRUD for Classes, Sections and Subjects and class↔subject mapping (`/api/academic/*`).
   - Reads allow `Admin` and `Teacher`; every write requires `Admin`.
   - List endpoints return the fields the UI needs without extra round trips: `SectionCount` on classes, `ClassId`/`ClassName` on sections, `TeacherName` on subjects.
   - Deletes are guarded. A class holding enrollments, a section holding enrollments, or a subject referenced by class-subject mappings, assignments, exams or timetable entries is refused with a count of what blocks it, rather than surfacing a foreign-key error. Deleting a class with no enrollments clears its sections and subject mappings in the same call.
   - Duplicate class names, duplicate section names within a class, and duplicate subject codes are rejected as `400` with a message naming the conflict. Section names are deliberately reusable across classes.
   - Setting a class's subjects replaces the whole set transactionally, and validates every id up front so a typo returns `400` naming the bad id and leaves the previous mapping untouched.
4. **Attendance Marking**: Per-section daily marking, correction of a marked day, marking history, and each student's own record (`/api/attendance/*`).
   - Staff (`Admin` and `Teacher`) load a roster for a class, section and date, mark each student, and save the day as one session. `GET /api/attendance/roster`, `GET /api/attendance/sessions`, `GET /api/attendance/sessions/{id}`, `POST /api/attendance/session`, `PUT /api/attendance/sessions/{id}`.
   - **One session per class, section and date.** A second mark for the same day is refused with `409` and the existing session id, so the UI opens the stored day for correction instead of adding another. Backed by `ux_attendancesessions_day`, since the duplicate used to double-count a day in a student's percentage.
   - A day's marks are written in one transaction, so a rejected record never leaves a half-marked session behind.
   - Exactly four statuses are accepted — `Present`, `Absent`, `Late`, `Excused`. Anything else is a `400`. The column was previously free text, so a typo was stored as a fifth status that no report counted and the student's percentage silently rose. `ux_attendancerecords_student` also makes the marks one-per-student-per-session, which is what lets a correction update in place instead of re-inserting.
   - Marks must belong to the section being marked; a stray id is refused with the offending ids named. An empty roster or a payload listing the same student twice is also refused.
   - Editing updates each mark in place, so a correction cannot duplicate a student or orphan their original remark, and a session cannot be moved to a different class or section.
   - `GET /api/attendance/mine` returns the signed-in student's own record without the page needing to know its `Students.Id`, which no other endpoint exposed.
   - `GET /api/attendance/student/{studentId}` is scoped through `StudentAccess`: staff may read anyone, a Student only themselves, a Parent only a child linked in `studentparents`. Every row carries `ClassName` and `SectionName` so the student and parent views need no second lookup.
5. **Teacher Features**: My Classes, My Subjects, Attendance history (`/api/teacher-portal/*`).
6. **Assignment System**: Creation, Attachments, Submissions, Grading & Feedback (`/api/assignments/*`).
7. **Examination & Results**: Exam Management, Marks Entry, Automated Percentage & Grading Calculation (`/api/exams/*`).
   - Staff (`Admin` and `Teacher`) create and edit exams, add each paper, load its class roster and mark it. `GET /api/exams`, `GET /api/exams/{id}`, `GET /api/exams/subjects/{examSubjectId}/roster`, `POST /api/exams`, `PUT /api/exams/{id}`, `POST /api/exams/{id}/subjects`, `DELETE /api/exams/{examId}/subjects/{examSubjectId}`, `PUT /api/exams/subjects/{examSubjectId}/marks`.
   - The whole marking flow was unreachable before this. Nothing could insert an `ExamSubjects` row, so the table was permanently empty, and the old marks handler read `MaxMarks` from a row that could not exist.
   - **A paper is one subject for one class, scoped by `ExamSubjects.ClassId`.** An exam may span several classes, which is why the link lives on the paper rather than the exam. Without it a Grade 5 mark could be filed against a Grade 11 paper. A class referenced by a paper cannot be deleted.
   - **A paper's whole roster saves in one transaction.** The old handler took one student per call, so marking a class of thirty meant thirty saves and a teacher who stopped halfway left a paper that looked finished. Marks are upserted on `ux_marks_examsubject_student`, so a re-save corrects in place rather than duplicating a row, and `ux_examsubjects_exam_class_subject` stops the same paper being added twice. Every save writes a `SAVE_MARKS` audit entry.
   - A mark must be inside `0..MaxMarks` and belong to the paper's own class; a stray id or an out-of-range value is a `400` naming the offending students, and nothing is written. The same student listed twice is also refused rather than saved twice with the last one silently winning.
   - Grades use fixed bands (`A+` 90, `A` 80, `B` 70, `C` 60, `F`), and `Exams.PassingMarks` is read as a **pass percentage**, not an absolute mark. The column defaulted to 40 and was previously never read at all.
   - Exam titles are unique per academic year, compared case-insensitively and by `AcademicYearId` rather than name — the seed data holds two years both named `2024-2025`, so a name comparison would collide them. Duplicate titles return `409`.
   - Deletes are guarded: an exam holding papers, or a paper holding marks, is refused with a count of what blocks it, because marks cascade from the paper and a blanket delete would quietly discard results. Only `Admin` may delete an exam.
   - `GET /api/exams` and `GET /api/exams/{id}` allow `Admin`, `Teacher` and `Student` but exclude `Parent`, matching the nav matrix: a parent sees their child's results and nothing else. Every row carries `ClassCount`, `SubjectCount` and `MarkCount`, because an exam with no papers is the state a new exam starts in and the list gave no way to tell that from a finished one. The list's mark count also had to move off `sum(... IS NOT NULL)`: PostgreSQL has no `sum(boolean)`, so it threw a `500` for every caller.
   - `GET /api/exams/mine` returns the signed-in student's own transcript without the page needing to know its `Students.Id`, which no other endpoint exposed. `GET /api/exams/student/{studentId}` is scoped through `StudentAccess`: staff may read anyone, a Student only themselves, a Parent only a child linked in `studentparents`.
   - Transcripts are rolled up server-side into one row per exam with `PassedCount`, `TotalObtained`, `TotalMax`, `OverallPercentage` and `Passed`. Each exam's own pass percentage is read per exam, so exams with different thresholds are not judged by one shared line. The legacy `GET /api/students/{id}/results` returned no exam id, date or class, so a transcript could not be grouped or ordered.
8. **Fee Management / Collection**: Fee structures, per-student or per-class assignment, and payments against the receivable ledger (`/api/fees/*`).
   - Admin-only. `GET/POST /api/fees/structures`, `PUT/DELETE /api/fees/structures/{id}`, `GET/POST /api/fees/assignments`, `DELETE /api/fees/assignments/{id}`, `GET/POST /api/fees/payments`, `GET /api/fees/summary`.
   - **The whole flow was unreachable before this.** Nothing inserted a `StudentFees` row, so the ledger was empty in every database, and the only payment path required an `Invoice` that nothing created either.
   - **`StudentFees` is the receivable ledger: one row per fee a student owes.** A payment now settles a `StudentFees` row directly (`Payments.StudentFeeId`), so paid, outstanding and status are sums over payments rather than a free-text `Status` column that no code path recomputed. That column is dropped.
   - **Status is derived on every read, never stored**, with one precedence shared by the admin list, the learner ledger and the collection report: `Paid` (paid ≥ amount), else `Overdue` (due date passed), else `Partial` (some paid), else `Unpaid`. `FeesController.Derive` is the single source of that rule.
   - Assignment is idempotent. `ux_studentfees_student_structure` makes re-assigning the same fee to the same student a no-op (the response carries `Assigned`/`Skipped` counts), so a class assignment can be re-run safely. A class-scoped fee can only be assigned to its own class.
   - **Overpayment is refused, not clamped**, naming the outstanding balance, so a mistyped amount comes back to the collector instead of becoming untraceable credit. The read-check-write locks the ledger row `FOR UPDATE`, so two collectors cannot jointly overpay it.
   - Deletes are guarded: a structure with assignments, or an assignment with payments, is refused with a count/amount of what blocks it, because the cascade would otherwise take the money trail with it. Changing a structure's amount is likewise refused once any student owes it.
   - Structure names are unique case-insensitively (`ux_feestructures_name`), amounts must be positive (`ck_feestructures_amount`), and a payment must name a target (`ck_payments_target`) with a positive amount (`ck_payments_amountpaid`).
   - `GET /api/students/{id}/fees` is the learner-scoped ledger, reading the same derived status through `StudentAccess`: staff may read anyone, a Student only themselves, a Parent only a linked child. `GET /api/reports/fee-collection` groups billed/collected/outstanding by that same status.
9. **Timetable & Schedule**: TimeSlots and TimetableEntries (`/api/schoolextensions/*`).
   - **The timetable was unreachable before this.** The only writer needed a `TimeSlotId` that no endpoint could create, so `TimetableEntries` was empty in every database, and the read endpoint demanded a class and section the learner pages had no way to resolve.
   - Admin manage the bell schedule (`GET/POST /api/schoolextensions/timeslots`, `PUT/DELETE /api/schoolextensions/timeslots/{id}`); every signed-in role may read it. A period must end after it starts and cannot overlap another (`ck_timeslots_range`), refused with 409.
   - Entry CRUD is Admin-only (`GET/POST /api/schoolextensions/timetable`, `PUT/DELETE /api/schoolextensions/timetable/{id}`). An entry must name a real day (ISO-8601 1–7, `ck_timetableentries_day`), a section of the chosen class, a subject that class actually offers (`ClassSubjects`), and an existing period; anything else is a 400.
   - Clashes are refused with 409, backed by `ux_timetableentries_section_slot` and `ux_timetableentries_teacher_slot`: a section sits in one subject per period per day, and a teacher teaches one class at a time. Updating an entry excludes itself from both checks, so re-saving is safe.
   - A period still scheduled into the timetable cannot be deleted — the `ON DELETE CASCADE` would silently wipe its entries — and is refused with the entry count.
   - Reads are scoped. `GET /api/schoolextensions/timetable?classId=&sectionId=` is for staff; `GET /api/schoolextensions/timetable/mine` resolves a Student's own class, or a Teacher's own lessons, server-side; `GET /api/schoolextensions/timetable/student/{id}` goes through `StudentAccess` (staff anyone, Student self, Parent linked child).
10. **Announcements & Notifications**: Announcements, Notifications, UserDevices (Mark read, delete, etc.) (`/api/announcements/*`, `/api/schoolextensions/notifications`).
11. **Events & Calendar**: Events, EventParticipants.
12. **Portals & Reports**: Student & Parent Portals (multi-child switching), Admin Reports (`/api/portals/*`, `/api/reports/*`). Reports include `students-by-class` and a `fee-collection` summary grouped by derived fee status.
13. **File Management & Audit Logs**: File metadata storage and administrative audit tracking (`/api/files/*`, `/api/auditlogs/*`).
14. **Security & DevOps**: Global Exception Handling, CORS, Docker containerization, .env support.

## Tech Stack

- **Framework**: ASP.NET Core 8 Web API
- **Database**: PostgreSQL with Dapper (micro-ORM)
- **Authentication**: JWT (Access Tokens), BCrypt password hashing
- **Configuration**: .env support via DotNetEnv
- **Containerization**: Docker / Docker Compose

## Getting Started

### Prerequisites
- .NET 8 SDK
- PostgreSQL 15+
- Docker Desktop (optional)

### Run with Docker
```bash
docker-compose up --build
```

### Run Locally
```bash
# Configure connection string in appsettings.json or .env
dotnet run --project SchoolHub.API
```

### Verify Build
```bash
dotnet build SchoolHub.API/SchoolHub.API.csproj
```

### Run the Tests
```bash
dotnet test SchoolHub.Tests/SchoolHub.Tests.csproj
```

The suite is an integration suite: it starts the API in-process and talks to a real
PostgreSQL database, so the connection string in `SchoolHub.Tests` must point at a
database that exists. It creates its own throwaway users (`t_admin`, `t_teacher`,
`t_student`, `t_parent`, ...) and removes them afterwards, so it can run against a
development database. It does not touch pre-existing rows outside the academic
fixtures it creates and cleans up.

Current coverage: authorization matrix for every role and endpoint, error mapping,
SQL column-name normalisation, student management and scoping, academic CRUD
including the delete guards and class-subject mapping, and the attendance marking
flow including the one-session-per-day guard, status validation, in-place editing
and learner scoping. The examination flow adds 30 tests covering exam CRUD and its
title conflict, the class-scoping guard, roster shape, mark bounds, atomic bulk
saves, in-place correction, the per-exam pass threshold, rollup grouping and learner
scoping. The fee-collection flow adds 15 tests covering structure validation,
class-scoping of assignment, idempotent re-assignment, the derived
`Paid`/`Partial`/`Overdue`/`Unpaid` precedence, refused overpayment, the delete
guards, the learner ledger and the grouped report. The timetable flow adds 10
tests covering period validation and overlap, entry validation (day, section,
offered subject, period), the section and teacher clash guards, in-place update,
the delete guard for a period in use, week ordering, the learner's own-class
read and its scoping, and admin-only writes.

Test classes that build throwaway academic rows (`AT-*`, `EX-*` and `TT-*` classes) clean
them up in `IAsyncLifetime.DisposeAsync` rather than only between their own tests, so they do
not change what other tests see when the whole suite runs in one pass.

## API Documentation

Swagger UI available at `/swagger` when running in Development mode.

## Database Schema

26 entities with full relationships:
- Users (with Role: Admin/Teacher/Student/Parent)
- Students, Teachers, Parents
- AcademicYears, Classes, Sections, Subjects
- TeacherSubjects, TimeSlots, TimetableEntries
- Assignments, AssignmentSubmissions
- AttendanceSessions, AttendanceRecords (one session per class/section/date, one mark per student per session)
- Exams, ExamSubjects (a paper is one subject for one class), Marks (one mark per student per paper)
- FeeStructures, StudentFees (the receivable ledger), Invoices, Payments (each settles a `StudentFees` row)
- Announcements, Notifications, UserDevices
- Events, EventParticipants
- Files, AuditLogs
- UserRefreshTokens

## Migrations

Schema changes that cannot be expressed as `CREATE TABLE IF NOT EXISTS` live in
`migrations/` as standalone SQL, applied in numeric order.

| File | What it does |
| --- | --- |
| `001_subjects_teacherid.sql` | Adds `Subjects.TeacherId` and backfills it from the existing teacher assignments. |
| `002_admin_password_and_login_guard.sql` | Moves the bootstrap admin to a hashed password and refuses login on an inactive account. |
| `003_enrollment_one_per_student.sql` | Adds `ux_enrollments_studentid`: a student belongs to exactly one class. |
| `004_merge_duplicate_classes.sql` | Merges the duplicate class rows in the dev database into one row per grade, repoints sections, enrollments, class-subject mappings, fee structures and attendance, and adds `ux_classes_name` / `ux_sections_class_name`. |
| `005_one_attendance_session_per_day.sql` | Collapses duplicate attendance sessions for the same class, section and date, carrying each duplicate's marks onto the newest session so none are lost, then adds `ux_attendancesessions_day` and `ux_attendancerecords_student`. |
| `006_scope_exam_subjects_to_class.sql` | Adds non-null `ExamSubjects.ClassId` referencing `Classes` with `ON DELETE RESTRICT`, collapsing any duplicate `(ExamId, SubjectId)` paper onto the newest row first so no marks are lost. Adds `ux_examsubjects_exam_class_subject`, `ux_marks_examsubject_student`, and the `ck_examsubjects_maxmarks` / `ck_marks_nonnegative` / `ck_exams_passingmarks` / `ck_exams_dates` checks. |
| `007_fees_ledger.sql` | Gives fees a write path: adds `Payments.StudentFeeId` (`ON DELETE CASCADE`), makes `StudentFees.StudentId`/`FeeStructureId` non-null (refusing if orphans exist), drops the stored `StudentFees.Status`, and adds `ux_studentfees_student_structure`, `ux_feestructures_name` and the `ck_payments_target` / `ck_feestructures_amount` / `ck_payments_amountpaid` checks. |
| `008_timetable.sql` | Gives the timetable a write path: adds the `ck_timeslots_range` and `ck_timetableentries_day` checks, `ux_timetableentries_section_slot` (one subject per section per period per day) and the partial `ux_timetableentries_teacher_slot` (one class per teacher per period per day). |

`004` is written to be re-runnable: every step is guarded by an existence check, so
a partial application can be resumed by running it again. `006` is guarded the same
way and was verified against the dev database twice with no change on the second run.
`007` is guarded with the same `pg_constraint` / `IF NOT EXISTS` checks and is safe to
re-run. `008` uses the same guards and was verified against the dev database twice
with no change on the second run.

Fresh databases get the equivalent guarantees from `DbInitializer`, which creates
`ux_enrollments_studentid`, `ux_classes_name`, `ux_sections_class_name`,
`ux_examsubjects_exam_class_subject` and `ux_marks_examsubject_student` alongside
the tables, plus the `007`/`008` fee and timetable constraints and indexes above,
and reads `Exams.PassingMarks` as a percentage between 0 and 100.
Apply the migrations before starting the API against an existing database.

## Response Casing

JSON uses PascalCase on the wire. `PropertyNamingPolicy` is set to `null` so typed
responses emit declared property names verbatim, and `DictionaryKeyPolicy` is set to
`SqlColumnNamingPolicy` to fix up untyped Dapper rows, whose keys are raw
PostgreSQL column names that the database folds to lower case.

That policy can only re-capitalise a key mechanically. Multi-word columns arrive
squashed with no separator (`classid`, `createdat`), so their word boundaries are
unrecoverable and each one is listed explicitly in `KnownColumns`
(`SchoolHub.API/Serialization/SqlColumnNaming.cs`). **Adding a multi-word alias to a
`QueryAsync` without a type argument means adding it to that dictionary too**, or the
client will receive a mis-cased key.

This applies to `AS` aliases as well as real columns, and the failure is silent in a
particular way. An alias written `u.Username as StudentName` reaches the policy as
`studentname`, and with no entry it falls through to the naive capitalisation and is
emitted as `Studentname` — a different key from the one the client reads, so the field
arrives as *missing* rather than as wrong. Three such aliases shipped that way
(`studentname`, `feename`, `totalstudentsmarked`); the parent child pickers on the
student and parent dashboards, the attendance page and the examinations page were all
rendering a blank name. Note that a plain "is it PascalCase" check passes for
`Studentname`, so `SqlColumnNamingTests` pins these names explicitly and also asserts
that the naive result is *not* returned.

A typed projection is unaffected: returning an anonymous object or a typed row emits
its own C# property names, so only rows handed straight to `Ok()` as a dynamic
`QueryAsync` result depend on the dictionary.