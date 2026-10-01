# SchoolHub Backend

Production-quality School Management System API built with C# / ASP.NET Core Web API, Dapper, and PostgreSQL.

## Status: ✅ Backend Complete

All milestones implemented with full CRUD, authentication, role-based access, and comprehensive feature coverage.

The API is covered by an xUnit integration suite (`SchoolHub.Tests`) that runs the real endpoints against a live PostgreSQL database. Current state: **146 passing tests**.

## Features & Module Coverage

1. **Authentication & Accounts**: Login, Register, JWT Access Tokens, Secure Password Hashing (BCrypt), Roles & Permissions, Profile Management & Picture (`/api/auth`, `/api/auth/password`, `/api/profile`).
2. **Admin Dashboard & Management**: Stats, Students, Teachers, Parents CRUD, Search, Filter, Assign Class/Section/Subjects (`/api/admin/*`).
3. **Academic Management**: Academic Years, plus Admin CRUD for Classes, Sections and Subjects and class↔subject mapping (`/api/academic/*`).
   - Reads allow `Admin` and `Teacher`; every write requires `Admin`.
   - List endpoints return the fields the UI needs without extra round trips: `SectionCount` on classes, `ClassId`/`ClassName` on sections, `TeacherName` on subjects.
   - Deletes are guarded. A class holding enrollments, a section holding enrollments, or a subject referenced by class-subject mappings, assignments, exams or timetable entries is refused with a count of what blocks it, rather than surfacing a foreign-key error. Deleting a class with no enrollments clears its sections and subject mappings in the same call.
   - Duplicate class names, duplicate section names within a class, and duplicate subject codes are rejected as `400` with a message naming the conflict. Section names are deliberately reusable across classes.
   - Setting a class's subjects replaces the whole set transactionally, and validates every id up front so a typo returns `400` naming the bad id and leaves the previous mapping untouched.
4. **Teacher Features**: My Classes, My Subjects, Attendance (`/api/teacher-portal/*`).
5. **Assignment System**: Creation, Attachments, Submissions, Grading & Feedback (`/api/assignments/*`).
6. **Examination & Results**: Exam Management, Marks Entry, Automated Percentage & Grading Calculation (`/api/exams/*`).
7. **Fee Management**: Fee Structures, Student Fees, Invoices, Payments (`/api/fees/*`).
8. **Timetable & Schedule**: TimeSlots, TimetableEntries.
9. **Announcements & Notifications**: Announcements, Notifications, UserDevices (Mark read, delete, etc.) (`/api/announcements/*`, `/api/schoolextensions/notifications`).
10. **Events & Calendar**: Events, EventParticipants.
11. **Portals & Reports**: Student & Parent Portals (multi-child switching), Admin Reports (`/api/portals/*`, `/api/reports/*`).
12. **File Management & Audit Logs**: File metadata storage and administrative audit tracking (`/api/files/*`, `/api/auditlogs/*`).
13. **Security & DevOps**: Global Exception Handling, CORS, Docker containerization, .env support.

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
SQL column-name normalisation, student management and scoping, and academic CRUD
including the delete guards and class-subject mapping.

## API Documentation

Swagger UI available at `/swagger` when running in Development mode.

## Database Schema

26 entities with full relationships:
- Users (with Role: Admin/Teacher/Student/Parent)
- Students, Teachers, Parents
- AcademicYears, Classes, Sections, Subjects
- TeacherSubjects, TimeSlots, TimetableEntries
- Assignments, AssignmentSubmissions
- Exams, ExamMarks
- FeeStructures, StudentFees, FeeInvoices, FeePayments
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

`004` is written to be re-runnable: every step is guarded by an existence check, so
a partial application can be resumed by running it again.

Fresh databases get the equivalent guarantees from `DbInitializer`, which creates
`ux_enrollments_studentid`, `ux_classes_name` and `ux_sections_class_name` alongside
the tables. Apply the migrations before starting the API against an existing
database.

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