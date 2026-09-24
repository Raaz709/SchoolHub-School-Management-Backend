using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolHub.API.Data;
using SchoolHub.API.DTOs;
using SchoolHub.API.Models;
using SchoolHub.API.Services;

namespace SchoolHub.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly SchoolHubDbContext _context;
        private readonly ITokenService _tokenService;

        public AuthController(SchoolHubDbContext context, ITokenService tokenService)
        {
            _context = context;
            _tokenService = tokenService;
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
        {
            var user = await _context.Users
                .Include(u => u.RefreshTokens)
                .FirstOrDefaultAsync(u => u.Username == request.Username);

            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return Unauthorized("Invalid username or password.");
            }

            var accessToken = _tokenService.CreateAccessToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken(user.Id);

            user.RefreshTokens.Add(refreshToken);
            await _context.SaveChangesAsync();

            return Ok(new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken.Token,
                Username = user.Username,
                Role = user.Role,
                UserId = user.Id
            });
        }

        [HttpPost("register")]
        public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
        {
            if (await _context.Users.AnyAsync(u => u.Username == request.Username))
            {
                return BadRequest("Username is already taken.");
            }

            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                Role = request.Role
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            if (request.Role == "Teacher")
            {
                _context.TeacherProfiles.Add(new TeacherProfile { UserId = user.Id, Department = request.Department });
            }
            else if (request.Role == "Student")
            {
                _context.StudentProfiles.Add(new StudentProfile { UserId = user.Id, ClassRoomId = request.ClassRoomId ?? 1, RollNumber = request.RollNumber });
            }
            else if (request.Role == "Parent")
            {
                _context.ParentProfiles.Add(new ParentProfile { UserId = user.Id });
            }

            await _context.SaveChangesAsync();

            var accessToken = _tokenService.CreateAccessToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken(user.Id);

            user.RefreshTokens.Add(refreshToken);
            await _context.SaveChangesAsync();

            return Ok(new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken.Token,
                Username = user.Username,
                Role = user.Role,
                UserId = user.Id
            });
        }

        [HttpPost("refresh-token")]
        public async Task<ActionResult<AuthResponse>> RefreshToken(RefreshTokenRequest request)
        {
            var refreshToken = await _context.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken);

            if (refreshToken == null || refreshToken.IsRevoked || refreshToken.IsExpired)
            {
                return Unauthorized("Invalid or expired refresh token.");
            }

            var user = refreshToken.User;
            var newAccessToken = _tokenService.CreateAccessToken(user);
            var newRefreshToken = _tokenService.GenerateRefreshToken(user.Id);

            refreshToken.IsRevoked = true;
            user.RefreshTokens.Add(newRefreshToken);
            await _context.SaveChangesAsync();

            return Ok(new AuthResponse
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken.Token,
                Username = user.Username,
                Role = user.Role,
                UserId = user.Id
            });
        }
    }
}
