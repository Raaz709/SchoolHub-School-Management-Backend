using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.Events;
using SchoolHub.API.Models.Auth;

namespace SchoolHub.API.Models.Events
{
    public class EventParticipant
    {
        [Key]
        [Column(Order = 0)]
        public int EventId { get; set; }

        [Key]
        [Column(Order = 1)]
        public int UserId { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Attending";

        [ForeignKey(nameof(EventId))]
        public Event Event { get; set; } = null!;

        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;
    }
}