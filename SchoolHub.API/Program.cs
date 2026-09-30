using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using SchoolHub.API.Data;
using SchoolHub.API.Middleware;
using SchoolHub.API.Serialization;
using SchoolHub.API.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// The React client is written against PascalCase members (userId, createdAt).
// ASP.NET Core defaults PropertyNamingPolicy to camelCase, which silently
// renamed every response property. Disable it so C# property names are emitted
// exactly as declared and the TS interfaces line up 1:1.
//
// DictionaryKeyPolicy covers the untyped Dapper rows, whose keys are raw
// PostgreSQL column names; see SqlColumnNaming for the details.
builder.Services.AddControllers().AddJsonOptions(options =>
{
    var json = options.JsonSerializerOptions;
    json.PropertyNamingPolicy = null;
    json.DictionaryKeyPolicy = new SqlColumnNamingPolicy();
    // Keep accepting camelCase on the way in so existing clients are unaffected.
    json.PropertyNameCaseInsensitive = true;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// PostgreSQL Connection String
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=00000";

// Register Dapper DbInitializer
builder.Services.AddSingleton<DbInitializer>(new DbInitializer(connectionString));

// JWT Authentication Service & Bearer Setup
builder.Services.AddScoped<ITokenService, TokenService>();

var jwtKey = builder.Configuration["Jwt:Key"] ?? "super_secret_key_for_schoolhub_jwt_security_token_2026_make_it_longer_123456!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "SchoolHubAPI",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "SchoolHubClient",
            ValidateLifetime = true
        };
    });

builder.Services.AddAuthorization();

// CORS Policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy", policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .SetIsOriginAllowed(_ => true);
    });
});

// Add Health Checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Initialize Database Schema with Dapper
using (var scope = app.Services.CreateScope())
{
    var dbInitializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
    await dbInitializer.InitializeAsync();
}

// Global Exception Handling Middleware
app.UseMiddleware<ExceptionMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("CorsPolicy");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();