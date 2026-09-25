# SchoolHub Backend

Production-quality School Management System API built with C# / ASP.NET Core Web API, Entity Framework Core, Dapper, and PostgreSQL.

## Milestones & Features

### Milestone 1: Database Setup with EF Core & PostgreSQL ✅
- **Entities Created**: 26 entities across 11 modules (Auth, People, Academic, Schedule, Attendance, Assignments, Exams, Fees, Communication, Events, System)
- **ApplicationDbContext**: Comprehensive Fluent API configuration with all relationships, foreign keys, indexes, unique constraints, and delete behaviors
- **EF Core Migration**: `InitialCreate` generated successfully with all 26 tables

### Milestone 2: Dapper Repository Pattern Implementation ✅
- **Base DapperRepository<T>**: Generic CRUD operations with parameterized queries (`@id`, `@name`, etc.)
- **Repository Interfaces**: `IUserRepository`, `IStudentRepository`, `ITeacherRepository`, `IParentRepository`, `IClassRepository`, `IRoleRepository`, `IRefreshTokenRepository`
- **Concrete Repositories**: `UserRepository`, `StudentRepository`, `TeacherRepository`, `ParentRepository`, `ClassRepository`, `RoleRepository`, `RefreshTokenRepository`
- **Parameterized Queries**: All SQL uses `@parameter` syntax for security and performance
- **AuthController Refactored**: Uses Dapper repositories instead of EF Core for all data access

### Milestone 3: Authentication & Authorization (In Progress)
- JWT Access Tokens + Refresh Tokens
- Role-based authorization (Admin, Teacher, Student, Parent)
- Secure password hashing with BCrypt
- Login, Register, Refresh Token endpoints

### Milestone 4: Admin Dashboard & Management (Planned)
- Student, Teacher, Parent CRUD
- Search, Filter, Pagination
- Assign Class/Section/Subjects

### Milestone 5: Academic & Core Modules (Planned)
- Attendance, Assignments, Exams & Grading
- Fee Management & Payments
- Timetable, Announcements, Notifications
- Reports & Analytics

## Architecture
- **Framework**: C# / ASP.NET Core Web API (.NET 8)
- **Database**: PostgreSQL with Entity Framework Core + Dapper
- **ORM**: EF Core with Fluent API (for migrations)
- **Data Access**: Dapper with parameterized queries (`@id`, `@name`, `@username`, etc.)
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
The application uses **Dapper** for all data access:
- **Repositories**: Generic `DapperRepository<T>` base class with parameterized queries
- **Parameterized Queries**: All SQL uses `@parameter` syntax (`@Id`, `@Username`, `@Email`, `@UserId`, etc.)
- **Connection Management**: `IDbConnection` injected via DI, scoped per request
- **AuthController**: Fully refactored to use Dapper repositories (`IUserRepository`, `IRoleRepository`, `IRefreshTokenRepository`, etc.)
- **EF Core**: Retained for migrations only