using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.Exams;
using SchoolHub.API.Models.People;

namespace SchoolHub.API.Models.Exams
{
    public class Mark
    {
        [Key]
        public int Id { get; set; }

        public int ExamSubjectId { get; set; }

        public int StudentId { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal MarksObtained { get; set; }

        [MaxLength(10)]
        public string? Grade { get; set; }

        [MaxLength(500)]
        public string? Remarks { get; set; }

        [ForeignKey(nameof(ExamSubjectId))]
        public ExamSubject ExamSubject { get; set; } = null!;

        [ForeignKey(nameof(StudentId))]
        public Student Student { get; set; } = null!;
    }
}