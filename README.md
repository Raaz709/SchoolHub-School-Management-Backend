# SchoolHub Backend

Production-quality School Management System API built with C# / ASP.NET Core Web API, Dapper, and PostgreSQL.

## Status: ✅ Backend Complete

All milestones implemented with full CRUD, authentication, role-based access, and comprehensive feature coverage.

## Features & Module Coverage

1. **Authentication & Accounts**: Login, Register, JWT Access Tokens, Secure Password Hashing (BCrypt), Roles & Permissions, Profile Management & Picture (`/api/auth`, `/api/auth/password`, `/api/profile`).
2. **Admin Dashboard & Management**: Stats, Students, Teachers, Parents CRUD, Search, Filter, Assign Class/Section/Subjects (`/api/admin/*`).
3. **Academic Management**: Academic Years, Classes, Sections, Subjects (`/api/academic/*`).
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