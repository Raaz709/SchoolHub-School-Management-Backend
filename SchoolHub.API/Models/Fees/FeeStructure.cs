using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.Academic;

namespace SchoolHub.API.Models.Fees
{
    public class FeeStructure
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Column(TypeName = "decimal(10,2)")]
        public decimal Amount { get; set; }

        public int ClassId { get; set; }

        [ForeignKey(nameof(ClassId))]
        public Class Class { get; set; } = null!;

        public ICollection<StudentFee> StudentFees { get; set; } = new List<StudentFee>();
    }
}