using Dapper;
using Npgsql;
using SchoolHub.API.Models.Auth;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace SchoolHub.API.Repositories
{
    public class RoleRepository : DapperRepository<Role>, IRoleRepository
    {
        public RoleRepository(IDbConnection dbConnection) : base(dbConnection, "Roles", "Id") { }

        public async Task<Role?> GetByNameAsync(string name)
        {
            return await Connection.QueryFirstOrDefaultAsync<Role>("SELECT * FROM Roles WHERE Name = @Name", new { Name = name });
        }
    }
}