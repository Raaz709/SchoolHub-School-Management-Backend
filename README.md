# SchoolHub Backend

Production-quality School Management System API built with C# / ASP.NET Core Web API, Entity Framework Core, Dapper, and PostgreSQL.

## Milestones & Features

### Milestone 1: Database Setup with EF Core & PostgreSQL ✅
- **Entities Created**: 26 entities across 11 modules (Auth, People, Academic, Schedule, Attendance, Assignments, Exams, Fees, Communication, Events, System)
- **ApplicationDbContext**: Comprehensive Fluent API configuration with all relationships, foreign keys, indexes, unique constraints, and delete behaviors
- **EF Core Migration**: `InitialCreate` generated successfully with all 26 tables
- **Build Status**: ✅ Succeeded (0 errors, 1 warning)

### Milestone 2: Authentication & Authorization (In Progress)
- JWT Access Tokens + Refresh Tokens
- Role-based authorization (Admin, Teacher, Student, Parent)
- Secure password hashing with BCrypt
- Login, Register, Refresh Token endpoints

### Milestone 3: Admin Dashboard & Management (Planned)
- Student, Teacher, Parent CRUD
- Search, Filter, Pagination
- Assign Class/Section/Subjects

### Milestone 4: Teacher/Student/Parent Portals (Planned)
- Dashboards with statistics
- Attendance, Assignments, Exams, Results, Fees
- Multi-child switching for parents

### Milestone 5: Academic & Core Modules (Planned)
- Attendance, Assignments, Exams & Grading
- Fee Management & Payments
- Timetable, Announcements, Notifications
- Reports & Analytics

## Architecture
- **Framework**: C# / ASP.NET Core Web API (.NET 8)
- **Database**: PostgreSQL with Entity Framework Core + Dapper
- **ORM**: EF Core with Fluent API
- **Authentication**: JWT + Refresh Tokens
- **API Documentation**: Swagger / OpenAPI
- **Health Check**: `/health` endpoint

## Configuration
All sensitive configuration is stored in `.env` file (not committed to git):

```bash
# Database Configuration
ConnectionStrings__DefaultConnection=Host=localhost;Port=5432;Database=school_management;Username=postgres;Password=00000

# JWT Configuration
Jwt__Key=super_secret_key_for_schoolhub_jwt_security_token_2026!
Jwt__Issuer=SchoolHubAPI
Jwt__Audience=SchoolHubClient

# App Settings
ASPNETCORE_ENVIRONMENT=Development
```

## Getting Started
1. Ensure PostgreSQL is running (locally or via Docker)
2. Copy `.env.example` to `.env` and update with your credentials
3. Run `dotnet ef database update` to apply EF Core migrations
4. Run `dotnet run --project SchoolHub.API` to start the API
5. Access Swagger at `https://localhost:5001/swagger`

## Database Schema
All 11 modules with 26 tables covering:
- **Auth**: Users, Roles, UserRoles, RefreshTokens
- **People**: Students, Parents, StudentParents, Teachers, Departments
- **Academic**: AcademicYears, Classes, Sections, Subjects, ClassSubjects, Enrollments
- **Schedule**: TimeSlots, TimetableEntries
- **Attendance**: AttendanceSessions, AttendanceRecords
- **Assignments**: Assignments, AssignmentSubmissions
- **Exams**: Exams, ExamSubjects, Marks, GradeScales
- **Fees**: FeeStructures, StudentFees, Invoices, Payments
- **Communication**: Announcements, Notifications, NotificationRecipients, UserDevices
- **Events**: Events, EventParticipants
- **System**: AuditLogs

## Dapper Integration
The application uses **Dapper** for:
- Database schema initialization on startup (DbInitializer)
- High-performance queries where needed
- EF Core is used for migrations and ORM operations