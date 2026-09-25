using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.Academic;
using SchoolHub.API.Models.People;

namespace SchoolHub.API.Models.Attendance
{
    public class AttendanceSession
    {
        [Key]
        public int Id { get; set; }

        public int ClassId { get; set; }

        public int SectionId { get; set; }

        public int? TeacherId { get; set; }

        public DateTime Date { get; set; } = DateTime.UtcNow;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(ClassId))]
        public Class Class { get; set; } = null!;

        [ForeignKey(nameof(SectionId))]
        public Section Section { get; set; } = null!;

        [ForeignKey(nameof(TeacherId))]
        public Teacher? Teacher { get; set; }

        public ICollection<AttendanceRecord> Records { get; set; } = new List<AttendanceRecord>();
    }
}