using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SchoolHub.API.Models.Exams;

namespace SchoolHub.API.Models.Academic
{
    public class AcademicYear
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Name { get; set; } = string.Empty;

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public bool IsCurrent { get; set; } = false;

        public ICollection<Exam> Exams { get; set; } = new List<Exam>();
        public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    }
}