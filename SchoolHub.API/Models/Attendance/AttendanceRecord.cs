using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.Attendance;
using SchoolHub.API.Models.People;

namespace SchoolHub.API.Models.Attendance
{
    public class AttendanceRecord
    {
        [Key]
        public int Id { get; set; }

        public int SessionId { get; set; }

        public int StudentId { get; set; }

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "Present";

        [MaxLength(500)]
        public string? Remarks { get; set; }

        [ForeignKey(nameof(SessionId))]
        public AttendanceSession Session { get; set; } = null!;

        [ForeignKey(nameof(StudentId))]
        public Student Student { get; set; } = null!;
    }
}