using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SchoolHub.API.Models.Schedule;
using SchoolHub.API.Models.Attendance;
using SchoolHub.API.Models.Fees;
using SchoolHub.API.Models.Academic;

namespace SchoolHub.API.Models.Academic
{
    public class Class
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public ICollection<Section> Sections { get; set; } = new List<Section>();
        public ICollection<Subject> Subjects { get; set; } = new List<Subject>();
        public ICollection<ClassSubject> ClassSubjects { get; set; } = new List<ClassSubject>();
        public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
        public ICollection<TimetableEntry> TimetableEntries { get; set; } = new List<TimetableEntry>();
        public ICollection<FeeStructure> FeeStructures { get; set; } = new List<FeeStructure>();
        public ICollection<AttendanceSession> AttendanceSessions { get; set; } = new List<AttendanceSession>();
    }
}