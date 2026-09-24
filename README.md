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
- **Student Management API**: CRUD endpoints for students (`/api/students`).
- **Teacher Management API**: CRUD endpoints for teachers (`/api/teachers`).
- **Academic API**: Management of Classes, Sections, and Subjects (`/api/academic/...`).
- **Attendance API**: Attendance session marking and records (`/api/attendance`).
- **Assignment API**: Assignment creation and student submissions (`/api/assignments`).
- **Examination & Results API**: Exam management, mark entry, and automated grading (`/api/exams`).
- **Fee Management API**: Fee structures and payment recording (`/api/fees`).
- **Announcements API**: Publishing announcements (`/api/announcements`).
- **Audit Logs API**: Tracking administrative actions (`/api/auditlogs`).
- **Documentation**: Swagger UI (`/swagger`) and Health Check (`/health`).

### Getting Started & Testing
1. Ensure Docker Desktop & PostgreSQL are available.
2. Run `docker-compose up --build` or `dotnet run --project SchoolHub.API`.
3. Verify build status: `dotnet build` (Succeeds with 0 errors).
