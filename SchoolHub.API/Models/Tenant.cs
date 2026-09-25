using System;

namespace SchoolHub.API.Models
{
    public class Tenant
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Identifier { get; set; } = string.Empty; // e.g. school code/slug
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
