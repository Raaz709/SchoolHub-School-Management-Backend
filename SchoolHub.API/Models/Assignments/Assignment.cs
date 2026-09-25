using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.Academic;
using SchoolHub.API.Models.People;

namespace SchoolHub.API.Models.Assignments
{
    public class Assignment
    {
        [Key]
        public int Id { get; set; }

        public int SubjectId { get; set; }

        public int TeacherId { get; set; }

        [Required]
        [MaxLength(255)]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public DateTime DueDate { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal? MaxScore { get; set; }

        [MaxLength(500)]
        public string? AttachmentUrl { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(SubjectId))]
        public Subject Subject { get; set; } = null!;

        [ForeignKey(nameof(TeacherId))]
        public Teacher Teacher { get; set; } = null!;

        public ICollection<AssignmentSubmission> Submissions { get; set; } = new List<AssignmentSubmission>();
    }
}