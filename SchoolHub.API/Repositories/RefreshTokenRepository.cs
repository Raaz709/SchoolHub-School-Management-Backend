using Dapper;
using Npgsql;
using SchoolHub.API.Models.Auth;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace SchoolHub.API.Repositories
{
    public class RefreshTokenRepository : DapperRepository<RefreshToken>, IRefreshTokenRepository
    {
        public RefreshTokenRepository(IDbConnection dbConnection) : base(dbConnection, "RefreshTokens", "Id") { }

        public async Task<RefreshToken?> GetByTokenAsync(string token)
        {
            return await Connection.QueryFirstOrDefaultAsync<RefreshToken>("SELECT * FROM RefreshTokens WHERE Token = @Token", new { Token = token });
        }

        public async Task<IEnumerable<RefreshToken>> GetByUserIdAsync(int userId)
        {
            return await Connection.QueryAsync<RefreshToken>("SELECT * FROM RefreshTokens WHERE UserId = @UserId", new { UserId = userId });
        }

        public async Task RevokeAllUserTokensAsync(int userId)
        {
            await Connection.ExecuteAsync("UPDATE RefreshTokens SET Revoked = CURRENT_TIMESTAMP WHERE UserId = @UserId AND Revoked IS NULL", new { UserId = userId });
        }
    }
}