using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.Auth;
using SchoolHub.API.Models.People;
using SchoolHub.API.Models.Academic;
using SchoolHub.API.Models.Schedule;
using SchoolHub.API.Models.Attendance;
using SchoolHub.API.Models.Assignments;

namespace SchoolHub.API.Models.People
{
    public class Teacher
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }

        public int? DepartmentId { get; set; }

        [Required]
        [MaxLength(50)]
        public string EmployeeCode { get; set; } = string.Empty;

        public DateTime HireDate { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        [ForeignKey(nameof(DepartmentId))]
        public Department? Department { get; set; }

        public ICollection<Subject> Subjects { get; set; } = new List<Subject>();
        public ICollection<TimetableEntry> TimetableEntries { get; set; } = new List<TimetableEntry>();
        public ICollection<AttendanceSession> AttendanceSessions { get; set; } = new List<AttendanceSession>();
        public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
    }
}