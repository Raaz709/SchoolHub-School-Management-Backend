using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.People;
using SchoolHub.API.Models.Schedule;
using SchoolHub.API.Models.Assignments;
using SchoolHub.API.Models.Exams;

namespace SchoolHub.API.Models.Academic
{
    public class Subject
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        public int? TeacherId { get; set; }

        [ForeignKey(nameof(TeacherId))]
        public Teacher? Teacher { get; set; }

        public ICollection<ClassSubject> ClassSubjects { get; set; } = new List<ClassSubject>();
        public ICollection<Exam> Exams { get; set; } = new List<Exam>();
        public ICollection<ExamSubject> ExamSubjects { get; set; } = new List<ExamSubject>();
        public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
        public ICollection<TimetableEntry> TimetableEntries { get; set; } = new List<TimetableEntry>();
    }
}