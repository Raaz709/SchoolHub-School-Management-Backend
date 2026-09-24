# SchoolHub Backend

Production-quality School Management System API built with C# / ASP.NET Core Web API, Entity Framework Core, and PostgreSQL.

## Milestone 1: Backend Repository Setup

### Architecture & Stack
- **Framework**: C# / ASP.NET Core Web API (.NET 8)
- **Database & ORM**: PostgreSQL with Entity Framework Core (multi-tenant ready design)
- **API Documentation**: Swagger / OpenAPI
- **Health Check**: `/health` endpoint

### Project Structure
- `SchoolHub.API/`
  - `Controllers/` - API endpoints (Health check)
  - `Data/` - EF Core `SchoolHubDbContext`
  - `Models/` - Domain entities (`Tenant`)
  - `Services/` - Business logic layer (to be expanded)
  - `DTOs/` - Data transfer objects (to be expanded)

### Configuration
Connection strings and application settings are configured via `appsettings.json` and environment variables (`ConnectionStrings__DefaultConnection`).

### Getting Started
1. Ensure .NET 8 SDK and PostgreSQL are installed.
2. Run `dotnet restore` and `dotnet build` inside `SchoolHub.API`.
3. Run `dotnet run --project SchoolHub.API` to start the API and access Swagger at `https://localhost:5001/swagger`.
