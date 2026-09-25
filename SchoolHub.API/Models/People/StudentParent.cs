using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.People;

namespace SchoolHub.API.Models.People
{
    public class StudentParent
    {
        [Key]
        [Column(Order = 0)]
        public int StudentId { get; set; }

        [Key]
        [Column(Order = 1)]
        public int ParentId { get; set; }

        [MaxLength(50)]
        public string? Relationship { get; set; }

        [ForeignKey(nameof(StudentId))]
        public Student Student { get; set; } = null!;

        [ForeignKey(nameof(ParentId))]
        public Parent Parent { get; set; } = null!;
    }
}