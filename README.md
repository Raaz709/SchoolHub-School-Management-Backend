# SchoolHub Backend

Production-quality School Management System API built with C# / ASP.NET Core Web API, Dapper, and PostgreSQL.

## Architecture & Database Schema (Dapper + PostgreSQL)

The database is structured into robust modular domains initialized automatically via Dapper on startup:

- **AUTH**: `Users`, `Roles` (Admin, Teacher, Student, Parent), `UserRoles`, `RefreshTokens`
- **PEOPLE**: `Students`, `Parents`, `StudentParents`, `Teachers`, `Departments`
- **ACADEMIC**: `AcademicYears`, `Classes`, `Sections`, `Subjects`, `ClassSubjects`, `Enrollments`
- **SCHEDULE**: `TimeSlots`, `TimetableEntries`
- **ATTENDANCE**: `AttendanceSessions`, `AttendanceRecords`
- **ASSIGNMENTS**: `Assignments`, `AssignmentSubmissions`
- **EXAMS**: `Exams`, `ExamSubjects`, `Marks`, `GradeScales`
- **FEES**: `FeeStructures`, `StudentFees`, `Invoices`, `Payments`
- **COMMUNICATION**: `Announcements`, `Notifications`, `NotificationRecipients`, `UserDevices`
- **EVENTS**: `Events`, `EventParticipants`
- **SYSTEM**: `AuditLogs`

## Status & Features
- **Project Setup**: Clean architecture (.NET 8 Web API).
- **Docker**: Containerized with PostgreSQL and API services (`docker-compose.up`).
- **Database & Dapper**: Full multi-domain schema auto-initialized on startup.
- **Authentication**: Register, Login, and JWT Access + Refresh Token rotation (`/api/auth`).
- **Documentation**: Swagger UI (`/swagger`) and Health Check (`/health`).

### Getting Started & Testing
1. Ensure Docker Desktop & PostgreSQL are available.
2. Run `docker-compose up --build` or `dotnet run --project SchoolHub.API`.
3. Verify build status: `dotnet build` (Succeeds with 0 errors).
