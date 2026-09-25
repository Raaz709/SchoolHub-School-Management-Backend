using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SchoolHub.API.Models.People;

namespace SchoolHub.API.Models.People
{
    public class Department
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        public ICollection<Teacher> Teachers { get; set; } = new List<Teacher>();
    }
}