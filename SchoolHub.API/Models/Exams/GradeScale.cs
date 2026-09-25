using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolHub.API.Models.Exams
{
    public class GradeScale
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(10)]
        public string Grade { get; set; } = string.Empty;

        [Column(TypeName = "decimal(5,2)")]
        public decimal MinPercentage { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal MaxPercentage { get; set; }
    }
}