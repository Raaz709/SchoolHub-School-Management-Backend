using SchoolHub.API.Models.Academic;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SchoolHub.API.Repositories
{
    public interface IClassRepository : IDapperRepository<Class>
    {
        Task<IEnumerable<Class>> GetAllWithSectionsAsync();
    }
}