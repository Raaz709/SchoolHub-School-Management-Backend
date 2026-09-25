using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolHub.API.Models.Academic;

namespace SchoolHub.API.Models.Academic
{
    public class ClassSubject
    {
        [Key]
        [Column(Order = 0)]
        public int ClassId { get; set; }

        [Key]
        [Column(Order = 1)]
        public int SubjectId { get; set; }

        [ForeignKey(nameof(ClassId))]
        public Class Class { get; set; } = null!;

        [ForeignKey(nameof(SubjectId))]
        public Subject Subject { get; set; } = null!;
    }
}