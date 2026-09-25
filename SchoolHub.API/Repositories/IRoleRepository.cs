using SchoolHub.API.Models.Auth;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SchoolHub.API.Repositories
{
    public interface IRoleRepository : IDapperRepository<Role>
    {
        Task<Role?> GetByNameAsync(string name);
    }
}