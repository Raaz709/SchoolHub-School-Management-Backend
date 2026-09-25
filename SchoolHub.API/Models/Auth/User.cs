using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SchoolHub.API.Models.People;

namespace SchoolHub.API.Models.Auth
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string PasswordHash { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? ProfilePictureUrl { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
        public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
        public Student? Student { get; set; }
        public Teacher? Teacher { get; set; }
        public Parent? Parent { get; set; }
    }
}