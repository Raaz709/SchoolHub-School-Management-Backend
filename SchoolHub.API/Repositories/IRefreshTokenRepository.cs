using SchoolHub.API.Models.Auth;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SchoolHub.API.Repositories
{
    public interface IRefreshTokenRepository : IDapperRepository<RefreshToken>
    {
        Task<RefreshToken?> GetByTokenAsync(string token);
        Task<IEnumerable<RefreshToken>> GetByUserIdAsync(int userId);
        Task RevokeAllUserTokensAsync(int userId);
    }
}