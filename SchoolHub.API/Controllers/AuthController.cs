using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolHub.API.Data;
using SchoolHub.API.DTOs;
using SchoolHub.API.Models.Auth;
using SchoolHub.API.Models.People;
using SchoolHub.API.Services;
using BCrypt.Net;

namespace SchoolHub.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ITokenService _tokenService;

        public AuthController(ApplicationDbContext context, ITokenService tokenService)
        {
            _context = context;
            _tokenService = tokenService;
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
        {
            var user = await _context.Users
                .Include(u => u.RefreshTokens)
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Username == request.Username);

            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return Unauthorized("Invalid username or password.");
            }

            var accessToken = _tokenService.CreateAccessToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken(user.Id);

            user.RefreshTokens.Add(refreshToken);
            await _context.SaveChangesAsync();

            var role = user.UserRoles.FirstOrDefault()?.Role.Name ?? "Student";

            return Ok(new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken.Token,
                Username = user.Username,
                Role = role,
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
                IsActive = true
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == request.Role);
            if (role == null)
            {
                role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "Student");
            }

            _context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
            await _context.SaveChangesAsync();

            if (request.Role == "Teacher")
            {
                _context.Teachers.Add(new Teacher
                {
                    UserId = user.Id,
                    DepartmentId = request.DepartmentId,
                    EmployeeCode = request.EmployeeCode ?? string.Empty
                });
            }
            else if (request.Role == "Student")
            {
                _context.Students.Add(new Student
                {
                    UserId = user.Id,
                    RollNumber = request.RollNumber ?? string.Empty
                });
            }
            else if (request.Role == "Parent")
            {
                _context.Parents.Add(new Parent { UserId = user.Id });
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
                Role = role.Name,
                UserId = user.Id
            });
        }

        [HttpPost("refresh-token")]
        public async Task<ActionResult<AuthResponse>> RefreshToken(RefreshTokenRequest request)
        {
            var refreshToken = await _context.RefreshTokens
                .Include(rt => rt.User)
                .ThenInclude(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
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

            var role = user.UserRoles.FirstOrDefault()?.Role.Name ?? "Student";

            return Ok(new AuthResponse
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken.Token,
                Username = user.Username,
                Role = role,
                UserId = user.Id
            });
        }
    }
}