using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolHub.API.DTOs;
using SchoolHub.API.Models.Auth;
using SchoolHub.API.Models.People;
using SchoolHub.API.Repositories;
using SchoolHub.API.Services;
using BCrypt.Net;

namespace SchoolHub.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly IRoleRepository _roleRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IStudentRepository _studentRepository;
        private readonly ITeacherRepository _teacherRepository;
        private readonly IParentRepository _parentRepository;
        private readonly ITokenService _tokenService;

        public AuthController(
            IUserRepository userRepository,
            IRoleRepository roleRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IStudentRepository studentRepository,
            ITeacherRepository teacherRepository,
            IParentRepository parentRepository,
            ITokenService tokenService)
        {
            _userRepository = userRepository;
            _roleRepository = roleRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _studentRepository = studentRepository;
            _teacherRepository = teacherRepository;
            _parentRepository = parentRepository;
            _tokenService = tokenService;
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
        {
            var user = await _userRepository.GetByUsernameWithRolesAsync(request.Username);

            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return Unauthorized("Invalid username or password.");
            }

            var accessToken = _tokenService.CreateAccessToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken(user.Id);

            await _refreshTokenRepository.CreateAsync(refreshToken);

            var role = user.UserRoles?.FirstOrDefault()?.Role?.Name ?? "Student";

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
            var existingUser = await _userRepository.GetByUsernameAsync(request.Username);
            if (existingUser != null)
            {
                return BadRequest("Username is already taken.");
            }

            var existingEmail = await _userRepository.GetByEmailAsync(request.Email);
            if (existingEmail != null)
            {
                return BadRequest("Email is already registered.");
            }

            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                IsActive = true
            };

            await _userRepository.CreateAsync(user);

            var role = await _roleRepository.GetByNameAsync(request.Role);
            if (role == null)
            {
                role = await _roleRepository.GetByNameAsync("Student");
            }

            await _userRepository.AssignRoleAsync(user.Id, role.Id);

            if (request.Role == "Teacher")
            {
                var teacher = new Teacher
                {
                    UserId = user.Id,
                    DepartmentId = request.DepartmentId,
                    EmployeeCode = request.EmployeeCode ?? string.Empty
                };
                // Note: Teacher creation would need ITeacherRepository.CreateAsync
                // For now, we'll handle this in a transaction or separate endpoint
            }
            else if (request.Role == "Student")
            {
                var student = new Student
                {
                    UserId = user.Id,
                    RollNumber = request.RollNumber ?? string.Empty
                };
                // Note: Student creation would need IStudentRepository.CreateAsync
            }
            else if (request.Role == "Parent")
            {
                var parent = new Parent { UserId = user.Id };
                // Note: Parent creation would need IParentRepository.CreateAsync
            }

            var accessToken = _tokenService.CreateAccessToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken(user.Id);

            await _refreshTokenRepository.CreateAsync(refreshToken);

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
            var refreshToken = await _refreshTokenRepository.GetByTokenAsync(request.RefreshToken);

            if (refreshToken == null || refreshToken.IsRevoked || refreshToken.IsExpired)
            {
                return Unauthorized("Invalid or expired refresh token.");
            }

            var user = await _userRepository.GetByUsernameWithRolesAsync(
                (await _userRepository.GetByIdAsync(refreshToken.UserId))?.Username ?? "");

            if (user == null)
            {
                return Unauthorized("User not found.");
            }

            var newAccessToken = _tokenService.CreateAccessToken(user);
            var newRefreshToken = _tokenService.GenerateRefreshToken(user.Id);

            await _refreshTokenRepository.UpdateAsync(refreshToken); // Mark old as revoked
            await _refreshTokenRepository.CreateAsync(newRefreshToken);

            var role = user.UserRoles?.FirstOrDefault()?.Role?.Name ?? "Student";

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