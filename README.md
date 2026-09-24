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

### Getting Started with Docker
1. Ensure Docker Desktop is running.
2. Run `docker-compose up --build` from the root directory to spin up PostgreSQL and the API.
