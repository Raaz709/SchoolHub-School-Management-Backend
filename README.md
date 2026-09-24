# SchoolHub Backend

Production-quality School Management System API built with C# / ASP.NET Core Web API, Dapper, and PostgreSQL.

## Architecture & Complete Database Schema (Dapper + PostgreSQL)

The database includes all requested domains and tables, initialized automatically via Dapper on startup:

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

## Complete Feature & API Coverage
- **Project Setup & Docker**: Clean architecture (.NET 8 Web API) containerized with Docker Compose.
- **Authentication & Authorization**: Register, Login, JWT Access Tokens, Refresh Token rotation, Role-based authorization (`/api/auth`).
- **Student Management**: CRUD & student profile registration (`/api/students`).
- **Teacher Management**: CRUD & teacher department assignment (`/api/teachers`).
- **Academic Management**: Academic years, classes, sections, and subjects (`/api/academic`, `/api/schoolextensions/academic-years`).
- **Attendance**: Session marking, student status, and history (`/api/attendance`).
- **Assignments**: Assignment creation and student submission handling (`/api/assignments`).
- **Exams & Results**: Exam management, mark entry, automated grading and calculation (`/api/exams`).
- **Fee Management**: Fee structures and payment recording (`/api/fees`).
- **Timetable & Schedule**: Timetable entries and time slots (`/api/schoolextensions/timetable`).
- **Announcements & Notifications**: Publishing announcements and system notifications (`/api/announcements`, `/api/schoolextensions/notifications`).
- **Events**: School events scheduling (`/api/schoolextensions/events`).
- **Audit Logs**: Administrative action tracking (`/api/auditlogs`).
- **Documentation & Health**: Swagger UI (`/swagger`) and Health Check (`/health`).

### Getting Started & Testing
1. Ensure Docker Desktop & PostgreSQL are available.
2. Run `docker-compose up --build` or `dotnet run --project SchoolHub.API`.
3. Verify build status: `dotnet build` (Succeeds with 0 errors).
