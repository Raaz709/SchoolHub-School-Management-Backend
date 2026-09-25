using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.Exams;
using SchoolHub.API.Models.Academic;

namespace SchoolHub.API.Models.Exams
{
    public class ExamSubject
    {
        [Key]
        public int Id { get; set; }

        public int ExamId { get; set; }

        public int SubjectId { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal MaxMarks { get; set; }

        public DateTime? ExamDate { get; set; }

        [ForeignKey(nameof(ExamId))]
        public Exam Exam { get; set; } = null!;

        [ForeignKey(nameof(SubjectId))]
        public Subject Subject { get; set; } = null!;

        public ICollection<Mark> Marks { get; set; } = new List<Mark>();
    }
}