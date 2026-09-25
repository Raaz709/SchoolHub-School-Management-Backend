using SchoolHub.API.Models.People;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SchoolHub.API.Repositories
{
    public interface ITeacherRepository : IDapperRepository<Teacher>
    {
        Task<Teacher?> GetByUserIdAsync(int userId);
        Task<Teacher?> GetByEmployeeCodeAsync(string employeeCode);
        Task<IEnumerable<Teacher>> GetByDepartmentAsync(int departmentId);
    }
}