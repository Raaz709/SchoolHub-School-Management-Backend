# SchoolHub Backend

Production-quality School Management System API built with C# / ASP.NET Core Web API, Entity Framework Core, and PostgreSQL.

## Milestones & Architecture

### Milestone 1: Backend Repository Setup
- C# / ASP.NET Core Web API (.NET 8)
- Clean architecture foundation (`Controllers`, `Services`, `Data`, `Models`, `DTOs`)
- Swagger / OpenAPI documentation & Health Check endpoint (`/health`)

### Milestone 2: PostgreSQL + EF Core & Docker Setup
- Configured Entity Framework Core with PostgreSQL (`Npgsql.EntityFrameworkCore.PostgreSQL`)
- Added `docker-compose.yml` and `Dockerfile` for containerizing PostgreSQL and ASP.NET Core API services.

### Milestone 3: Database Entities & Initial Migration
- Created comprehensive domain models (`User`, `RefreshToken`, `ClassRoom`, `StudentProfile`, `TeacherProfile`, `ParentProfile`, `Subject`, `Attendance`, `Exam`, `ExamResult`, `Fee`, `Notice`, `EventItem`, `Tenant`).
- Configured EF Core `SchoolHubDbContext` with relationships and indexes.
- Created initial EF Core migration (`InitialCreate`).

### Milestone 4 & 5: Authentication API & JWT + Refresh Tokens
- Implemented `AuthController` (`/api/auth/login`, `/api/auth/register`, `/api/auth/refresh-token`).
- Implemented `TokenService` for generating short-lived JWT Access Tokens and secure crypto random Refresh Tokens with rotation.
- Configured ASP.NET Core JWT Bearer authentication middleware.

### Getting Started with Docker
1. Ensure Docker Desktop is running.
2. Run `docker-compose up --build` from the root directory to spin up PostgreSQL and the API.
