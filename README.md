# SchoolHub Backend

Production-quality School Management System API built with C# / ASP.NET Core Web API, Dapper, and PostgreSQL.

## Architecture & Database Schema (Dapper + PostgreSQL)

The database is structured into robust modular domains initialized via Dapper:

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

## Milestones & Features
1. **Backend Repository Setup**: Clean architecture, Swagger, Health Checks (`/health`).
2. **PostgreSQL & Docker Setup**: Docker Compose configuration for PostgreSQL and API.
3. **Database Schema & Dapper**: Automated Dapper-based table initialization across all requested domains and roles on startup.
4. **Authentication & JWT**: Secure login, registration, and token rotation using Dapper and JWT Bearer authentication.

### Getting Started with Docker
1. Ensure Docker Desktop is running.
2. Run `docker-compose up --build` from the root directory to spin up PostgreSQL and the API.
