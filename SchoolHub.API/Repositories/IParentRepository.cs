using SchoolHub.API.Models.People;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SchoolHub.API.Repositories
{
    public interface IParentRepository : IDapperRepository<Parent>
    {
        Task<Parent?> GetByUserIdAsync(int userId);
        Task<IEnumerable<Student>> GetChildrenAsync(int parentId);
    }
}