# SchoolHub Backend

Production-quality School Management System API built with C# / ASP.NET Core Web API, Dapper, and PostgreSQL.

## Milestones & Features (Password Management)

### Feature Added: Password Management API (`/api/auth/password`)
- **Forgot Password**: Generates secure password reset tokens (`POST /api/auth/password/forgot`).
- **Reset Password**: Validates reset tokens and updates user password securely (`POST /api/auth/password/reset`).
- **Change Password**: Authenticated endpoint to change password with current password verification (`POST /api/auth/password/change`).

### Complete Database Schema & Domains (Dapper + PostgreSQL)
- **AUTH**: `Users`, `Roles`, `UserRoles`, `RefreshTokens`, `PasswordResetTokens`
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
