using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.People;
using SchoolHub.API.Models.Academic;

namespace SchoolHub.API.Models.Academic
{
    public class Enrollment
    {
        [Key]
        public int Id { get; set; }

        public int StudentId { get; set; }

        public int ClassId { get; set; }

        public int? SectionId { get; set; }

        public int AcademicYearId { get; set; }

        public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(StudentId))]
        public Student Student { get; set; } = null!;

        [ForeignKey(nameof(ClassId))]
        public Class Class { get; set; } = null!;

        [ForeignKey(nameof(SectionId))]
        public Section? Section { get; set; }

        [ForeignKey(nameof(AcademicYearId))]
        public AcademicYear AcademicYear { get; set; } = null!;
    }
}