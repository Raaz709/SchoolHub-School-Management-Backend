using SchoolHub.API.Models.Auth;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SchoolHub.API.Repositories
{
    public interface IUserRepository : IDapperRepository<User>
    {
        Task<User?> GetByUsernameAsync(string username);
        Task<User?> GetByEmailAsync(string email);
        Task<User?> GetByUsernameWithRolesAsync(string username);
        Task<IEnumerable<Role>> GetUserRolesAsync(int userId);
        Task AssignRoleAsync(int userId, int roleId);
        Task RemoveRoleAsync(int userId, int roleId);
    }
}