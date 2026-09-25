using SchoolHub.API.Models.People;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SchoolHub.API.Repositories
{
    public interface IStudentRepository : IDapperRepository<Student>
    {
        Task<Student?> GetByUserIdAsync(int userId);
        Task<Student?> GetByRollNumberAsync(string rollNumber);
        Task<IEnumerable<Student>> GetByClassAsync(int classId);
        Task<IEnumerable<Student>> GetBySectionAsync(int sectionId);
    }
}