# SchoolHub Backend

Production-quality School Management System API built with C# / ASP.NET Core Web API, Dapper, and PostgreSQL.

## Architecture & Complete Database Schema (Dapper + PostgreSQL)

The database includes all requested domains and tables, initialized automatically via Dapper on startup:

- **AUTH**: `Users`, `Roles` (Admin, Teacher, Student, Parent), `UserRoles`, `RefreshTokens`, `PasswordResetTokens`
- **PEOPLE**: `Students`, `Parents`, `StudentParents`, `Teachers`, `Departments`
- **ACADEMIC**: `AcademicYears`, `Classes`, `Sections`, `Subjects`, `ClassSubjects`, `Enrollments`
- **SCHEDULE**: `TimeSlots`, `TimetableEntries`
- **ATTENDANCE**: `AttendanceSessions`, `AttendanceRecords`
- **ASSIGNMENTS**: `Assignments`, `AssignmentSubmissions`
- **EXAMS**: `Exams`, `ExamSubjects`, `Marks`, `GradeScales`
- **FEES**: `FeeStructures`, `StudentFees`, `Invoices`, `Payments`
- **COMMUNICATION**: `Announcements`, `Notifications`, `UserDevices`
- **EVENTS**: `Events`, `EventParticipants`
- **SYSTEM**: `FilesMetadata`, `AuditLogs`

## Features & API Coverage
- **Admin Management API (`/api/admin/teachers`, `/api/admin/parents`)**:
  - Add, edit, view, and deactivate teachers.
  - Assign subjects to teachers.
  - Add, edit, link/unlink parents to students, and view parent's children.
- **Student Management API (`/api/students`)**: Add, edit, view, deactivate, search, filter, assign class, view attendance, results, fees, and assignments.
- **Authentication & Security (`/api/auth`)**: Login, Register, JWT Access & Refresh Tokens.
- **Academic, Attendance, Assignments, Exams, Fees, Timetable, Announcements, Events, Audit Logs**: Full multi-domain API support.
- **Documentation & Health**: Swagger UI (`/swagger`) and Health Check (`/health`).

### Getting Started & Testing
1. Ensure Docker Desktop & PostgreSQL are available.
2. Run `docker-compose up --build` or `dotnet run --project SchoolHub.API`.
3. Verify build status: `dotnet build` (Succeeds with 0 errors).
