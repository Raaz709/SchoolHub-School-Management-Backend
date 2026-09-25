using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.Auth;
using SchoolHub.API.Models.Academic;
using SchoolHub.API.Models.Attendance;
using SchoolHub.API.Models.Assignments;
using SchoolHub.API.Models.Exams;
using SchoolHub.API.Models.Fees;
using SchoolHub.API.Models.People;

namespace SchoolHub.API.Models.People
{
    public class Student
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }

        [Required]
        [MaxLength(50)]
        public string RollNumber { get; set; } = string.Empty;

        public DateTime AdmissionDate { get; set; } = DateTime.UtcNow;

        public int? ParentId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        [ForeignKey(nameof(ParentId))]
        public Parent? Parent { get; set; }

        public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
        public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
        public ICollection<AssignmentSubmission> AssignmentSubmissions { get; set; } = new List<AssignmentSubmission>();
        public ICollection<Mark> Marks { get; set; } = new List<Mark>();
        public ICollection<StudentFee> StudentFees { get; set; } = new List<StudentFee>();
        public ICollection<StudentParent> StudentParents { get; set; } = new List<StudentParent>();
    }
}