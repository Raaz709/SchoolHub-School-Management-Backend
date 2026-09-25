using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.People;
using SchoolHub.API.Models.Fees;

namespace SchoolHub.API.Models.Fees
{
    public class Invoice
    {
        [Key]
        public int Id { get; set; }

        public int StudentId { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal TotalAmount { get; set; }

        public DateTime IssuedDate { get; set; } = DateTime.UtcNow;

        public DateTime DueDate { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Unpaid";

        [ForeignKey(nameof(StudentId))]
        public Student Student { get; set; } = null!;

        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}