using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SchoolHub.API.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public string Username { get; set; } = string.Empty;
        [Required]
        public string Email { get; set; } = string.Empty;
        [Required]
        public string PasswordHash { get; set; } = string.Empty;
        [Required]
        public string Role { get; set; } = "Student"; // Admin, Teacher, Student, Parent
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    }

    public class RefreshToken
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        [Required]
        public string Token { get; set; } = string.Empty;
        public DateTime Expires { get; set; }
        public bool IsExpired => DateTime.UtcNow >= Expires;
        public DateTime Created { get; set; } = DateTime.UtcNow;
        public bool IsRevoked { get; set; } = false;
    }

    public class ClassRoom
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public string Name { get; set; } = string.Empty; // e.g. "Grade 10"
        [Required]
        public string Section { get; set; } = string.Empty; // e.g. "A"
        public int? TeacherId { get; set; } // Class Teacher
        public TeacherProfile? Teacher { get; set; }
        public ICollection<StudentProfile> Students { get; set; } = new List<StudentProfile>();
        public ICollection<Subject> Subjects { get; set; } = new List<Subject>();
    }

    public class StudentProfile
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public int ClassRoomId { get; set; }
        public ClassRoom ClassRoom { get; set; } = null!;
        [Required]
        public string RollNumber { get; set; } = string.Empty;
        public int? ParentId { get; set; }
        public ParentProfile? Parent { get; set; }
        public ICollection<Attendance> Attendances { get; set; } = new List<Attendance>();
        public ICollection<ExamResult> ExamResults { get; set; } = new List<ExamResult>();
        public ICollection<Fee> Fees { get; set; } = new List<Fee>();
    }

    public class TeacherProfile
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        [Required]
        public string Department { get; set; } = string.Empty;
        public ICollection<Subject> Subjects { get; set; } = new List<Subject>();
        public ICollection<ClassRoom> ManagedClasses { get; set; } = new List<ClassRoom>();
    }

    public class ParentProfile
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public ICollection<StudentProfile> Children { get; set; } = new List<StudentProfile>();
    }

    public class Subject
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public string Name { get; set; } = string.Empty;
        public int ClassRoomId { get; set; }
        public ClassRoom ClassRoom { get; set; } = null!;
        public int TeacherId { get; set; }
        public TeacherProfile Teacher { get; set; } = null!;
        public ICollection<Exam> Exams { get; set; } = new List<Exam>();
    }

    public class Attendance
    {
        [Key]
        public int Id { get; set; }
        public int StudentId { get; set; }
        public StudentProfile Student { get; set; } = null!;
        public int ClassRoomId { get; set; }
        public ClassRoom ClassRoom { get; set; } = null!;
        public DateTime Date { get; set; }
        [Required]
        public string Status { get; set; } = "Present"; // Present, Absent, Late
        public int MarkedById { get; set; }
        public User MarkedBy { get; set; } = null!;
    }

    public class Exam
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public string Title { get; set; } = string.Empty;
        public int SubjectId { get; set; }
        public Subject Subject { get; set; } = null!;
        public int ClassRoomId { get; set; }
        public ClassRoom ClassRoom { get; set; } = null!;
        public DateTime Date { get; set; }
        public decimal TotalMarks { get; set; }
        public ICollection<ExamResult> Results { get; set; } = new List<ExamResult>();
    }

    public class ExamResult
    {
        [Key]
        public int Id { get; set; }
        public int ExamId { get; set; }
        public Exam Exam { get; set; } = null!;
        public int StudentId { get; set; }
        public StudentProfile Student { get; set; } = null!;
        public decimal MarksObtained { get; set; }
        public string Grade { get; set; } = string.Empty;
    }

    public class Fee
    {
        [Key]
        public int Id { get; set; }
        public int StudentId { get; set; }
        public StudentProfile Student { get; set; } = null!;
        public decimal Amount { get; set; }
        public DateTime DueDate { get; set; }
        [Required]
        public string Status { get; set; } = "Pending"; // Pending, Paid
        public DateTime? PaidDate { get; set; }
    }

    public class Notice
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public string Title { get; set; } = string.Empty;
        [Required]
        public string Content { get; set; } = string.Empty;
        [Required]
        public string TargetRole { get; set; } = "All"; // All, Admin, Teacher, Student, Parent
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class EventItem
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public string Title { get; set; } = string.Empty;
        [Required]
        public string Description { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
