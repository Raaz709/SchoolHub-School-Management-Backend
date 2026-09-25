using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.Academic;

namespace SchoolHub.API.Models.Exams
{
    public class Exam
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(255)]
        public string Title { get; set; } = string.Empty;

        public int AcademicYearId { get; set; }

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal PassingMarks { get; set; } = 40.00m;

        [ForeignKey(nameof(AcademicYearId))]
        public AcademicYear AcademicYear { get; set; } = null!;

        public ICollection<ExamSubject> ExamSubjects { get; set; } = new List<ExamSubject>();
    }
}