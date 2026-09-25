using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.People;
using SchoolHub.API.Models.Fees;

namespace SchoolHub.API.Models.Fees
{
    public class StudentFee
    {
        [Key]
        public int Id { get; set; }

        public int StudentId { get; set; }

        public int FeeStructureId { get; set; }

        public DateTime DueDate { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Unpaid";

        [ForeignKey(nameof(StudentId))]
        public Student Student { get; set; } = null!;

        [ForeignKey(nameof(FeeStructureId))]
        public FeeStructure FeeStructure { get; set; } = null!;
    }
}