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

## Complete Feature & Admin Capabilities
- **Admin Student Management**:
  - Add student (`POST /api/students`)
  - Edit student (`PUT /api/students/{id}`)
  - View student details & list (`GET /api/students`, `GET /api/students/{id}`)
  - Delete/deactivate student (`PATCH /api/students/{id}/deactivate`)
  - Search & filter students (`GET /api/students/search?query=...&classId=...&sectionId=...`)
  - Assign student to class/section (`POST /api/students/{id}/assign-class`)
  - View student attendance (`GET /api/students/{id}/attendance`)
  - View student results (`GET /api/students/{id}/results`)
  - View student fees (`GET /api/students/{id}/fees`)
  - View student assignments (`GET /api/students/{id}/assignments`)
- **Other Modules**: Teachers, Academic, Attendance, Assignments, Exams, Fees, Timetable, Announcements, Events, Audit Logs.
- **Documentation & Health**: Swagger UI (`/swagger`) and Health Check (`/health`).

### Getting Started & Testing
1. Ensure Docker Desktop & PostgreSQL are available.
2. Run `docker-compose up --build` or `dotnet run --project SchoolHub.API`.
3. Verify build status: `dotnet build` (Succeeds with 0 errors).
