using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.Fees;

namespace SchoolHub.API.Models.Fees
{
    public class Payment
    {
        [Key]
        public int Id { get; set; }

        public int InvoiceId { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal AmountPaid { get; set; }

        public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

        [MaxLength(50)]
        public string? PaymentMethod { get; set; }

        [MaxLength(255)]
        public string? TransactionReference { get; set; }

        [ForeignKey(nameof(InvoiceId))]
        public Invoice Invoice { get; set; } = null!;
    }
}