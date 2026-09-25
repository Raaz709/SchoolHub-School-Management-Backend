using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.Auth;
using SchoolHub.API.Models.People;

namespace SchoolHub.API.Models.People
{
    public class Parent
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }

        [MaxLength(100)]
        public string? Occupation { get; set; }

        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        public ICollection<StudentParent> StudentParents { get; set; } = new List<StudentParent>();
    }
}