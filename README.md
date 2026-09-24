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

## Complete Feature & API Coverage
- **Teacher Portal API (`/api/teacher/...`)**: My classes, subjects, attendance history.
- **Student Portal API (`/api/portals/student/dashboard`)**: Attendance percentage, pending assignments, upcoming exam, unread notifications.
- **Parent Portal API (`/api/portals/parent/children`)**: Multi-child switching and child overview.
- **Reports API (`/api/reports/...`)**: Students by class/section analytics and fee collection reports.
- **Admin Management API**: Teachers, parents, classes, subjects, attendance, assignments, exams, fees, timetable, announcements, events, audit logs.
- **Authentication & Security (`/api/auth`)**: Login, Register, JWT Access & Refresh Tokens.
- **Documentation & Health**: Swagger UI (`/swagger`) and Health Check (`/health`).

### Getting Started & Testing
1. Ensure Docker Desktop & PostgreSQL are available.
2. Run `docker-compose up --build` or `dotnet run --project SchoolHub.API`.
3. Verify build status: `dotnet build` (Succeeds with 0 errors).
