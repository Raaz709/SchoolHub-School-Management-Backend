# SchoolHub Backend

Production-quality School Management System API built with C# / ASP.NET Core Web API, Dapper, and PostgreSQL.

## Milestones & Features (Profile Management)

### Feature Added: Profile Management API (`/api/profile`)
- **View Profile**: Get authenticated user profile details (`GET /api/profile`).
- **Update Profile**: Update username and email (`PUT /api/profile`).
- **Profile Picture**: Update user profile picture URL (`POST /api/profile/picture`).

### Complete Feature & Module Coverage
1. **Authentication & Accounts**: Login, Logout, Register, Forgot/Reset/Change Password, JWT Access & Refresh Tokens, Secure Password Hashing, Activation/Deactivation, Roles & Permissions, Profile Management & Picture (`/api/auth`, `/api/auth/password`, `/api/profile`).
2. **Admin Dashboard & Management**: Students, Teachers, Parents CRUD, Search, Filter, Assign Class/Section/Subjects.
3. **Academic Management**: Academic Years, Classes, Sections, Subjects.
4. **Teacher Features**: My Classes, My Subjects, Attendance.
5. **Assignment System**: Creation, Attachments, Submissions, Grading & Feedback.
6. **Examination & Results**: Exam Management, Marks Entry, Automated Percentage & Grading Calculation.
7. **Fee Management**: Fee Structures, Student Fees, Invoices, Payments.
8. **Timetable & Schedule**: TimeSlots, TimetableEntries.
9. **Announcements & Notifications**: Announcements, Notifications, UserDevices.
10. **Events & Calendar**: Events, EventParticipants.
11. **Portals & Reports**: Student & Parent Portals (multi-child switching), Admin Reports.
12. **File Management & Audit Logs**: File metadata storage and administrative audit tracking.
13. **Security & DevOps**: Global Exception Handling, CORS, Docker containerization.

### Getting Started & Testing
1. Ensure Docker Desktop & PostgreSQL are available.
2. Run `docker-compose up --build` or `dotnet run --project SchoolHub.API`.
3. Verify build status: `dotnet build` (Succeeds with 0 errors).
