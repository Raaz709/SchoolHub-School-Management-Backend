using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.Assignments;
using SchoolHub.API.Models.People;

namespace SchoolHub.API.Models.Assignments
{
    public class AssignmentSubmission
    {
        [Key]
        public int Id { get; set; }

        public int AssignmentId { get; set; }

        public int StudentId { get; set; }

        [MaxLength(500)]
        public string? FilePath { get; set; }

        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

        [Column(TypeName = "decimal(5,2)")]
        public decimal? Score { get; set; }

        public string? Feedback { get; set; }

        [ForeignKey(nameof(AssignmentId))]
        public Assignment Assignment { get; set; } = null!;

        [ForeignKey(nameof(StudentId))]
        public Student Student { get; set; } = null!;
    }
}