using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;
using System.Security.Claims;

namespace SchoolHub.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class FilesController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly IWebHostEnvironment _env;

        public FilesController(IConfiguration configuration, IWebHostEnvironment env)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Database=schoolhub_db;Username=postgres;Password=postgres";
            _env = env;
        }

        private IDbConnection Connection => new NpgsqlConnection(_connectionString);

        [HttpPost("upload")]
        public async Task<IActionResult> UploadFile(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("No file uploaded.");

            var uploadsFolder = Path.Combine(_env.ContentRootPath, "Uploads");
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

            var uniqueName = $"{Guid.NewGuid()}_{file.FileName}";
            var filePath = Path.Combine(uploadsFolder, uniqueName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(userIdStr, out int userId);

            using var db = Connection;
            var sql = @"
                INSERT INTO FilesMetadata (FileName, FilePath, ContentType, UploadedById) 
                VALUES (@FileName, @FilePath, @ContentType, @UploadedById) 
                RETURNING Id;";
            var fileId = await db.ExecuteScalarAsync<int>(sql, new {
                FileName = file.FileName,
                FilePath = filePath,
                ContentType = file.ContentType,
                UploadedById = userId
            });

            return Ok(new { Message = "File uploaded successfully", FileId = fileId, FileName = uniqueName });
        }
    }
}
