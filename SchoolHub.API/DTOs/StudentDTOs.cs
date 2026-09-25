using System.ComponentModel.DataAnnotations;

namespace SchoolHub.API.DTOs
{
    public class CreateStudentDto
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string RollNumber { get; set; } = string.Empty;

        public int? ClassId { get; set; }

        public int? SectionId { get; set; }

        public int? ParentId { get; set; }
    }

    public class UpdateStudentDto
    {
        public string Email { get; set; } = string.Empty;
        public string RollNumber { get; set; } = string.Empty;
        public int? ClassId { get; set; }
        public int? SectionId { get; set; }
        public int? ParentId { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class StudentSearchDto
    {
        public string? SearchTerm { get; set; }
        public int? ClassId { get; set; }
        public int? SectionId { get; set; }
        public bool? IsActive { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }

    public class AssignClassDto
    {
        public int ClassId { get; set; }
        public int SectionId { get; set; }
    }

    public class PagedResult<T>
    {
        public IEnumerable<T> Data { get; set; } = new List<T>();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }
}