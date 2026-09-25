using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.Communication;
using SchoolHub.API.Models.Auth;

namespace SchoolHub.API.Models.Communication
{
    public class NotificationRecipient
    {
        [Key]
        [Column(Order = 0)]
        public int NotificationId { get; set; }

        [Key]
        [Column(Order = 1)]
        public int UserId { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime? ReadAt { get; set; }

        [ForeignKey(nameof(NotificationId))]
        public Notification Notification { get; set; } = null!;

        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;
    }
}