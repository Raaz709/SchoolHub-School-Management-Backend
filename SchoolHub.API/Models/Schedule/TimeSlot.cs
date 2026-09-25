using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SchoolHub.API.Models.Academic;

namespace SchoolHub.API.Models.Schedule
{
    public class TimeSlot
    {
        [Key]
        public int Id { get; set; }

        public TimeOnly StartTime { get; set; }

        public TimeOnly EndTime { get; set; }

        [MaxLength(50)]
        public string? Label { get; set; }

        public ICollection<TimetableEntry> TimetableEntries { get; set; } = new List<TimetableEntry>();
    }
}