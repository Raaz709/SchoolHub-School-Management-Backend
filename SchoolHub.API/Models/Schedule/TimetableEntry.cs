using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.Academic;
using SchoolHub.API.Models.People;

namespace SchoolHub.API.Models.Schedule
{
    public class TimetableEntry
    {
        [Key]
        public int Id { get; set; }

        public int ClassId { get; set; }

        public int SectionId { get; set; }

        public int SubjectId { get; set; }

        public int? TeacherId { get; set; }

        public int TimeSlotId { get; set; }

        public int DayOfWeek { get; set; }

        [ForeignKey(nameof(ClassId))]
        public Class Class { get; set; } = null!;

        [ForeignKey(nameof(SectionId))]
        public Section Section { get; set; } = null!;

        [ForeignKey(nameof(SubjectId))]
        public Subject Subject { get; set; } = null!;

        [ForeignKey(nameof(TeacherId))]
        public Teacher? Teacher { get; set; }

        [ForeignKey(nameof(TimeSlotId))]
        public TimeSlot TimeSlot { get; set; } = null!;
    }
}