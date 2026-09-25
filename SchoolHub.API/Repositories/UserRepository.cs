using Dapper;
using Npgsql;
using SchoolHub.API.Models.Auth;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace SchoolHub.API.Repositories
{
    public class UserRepository : DapperRepository<User>, IUserRepository
    {
        public UserRepository(IDbConnection dbConnection) : base(dbConnection, "Users", "Id") { }

        public async Task<User?> GetByUsernameAsync(string username)
        {
            return await Connection.QueryFirstOrDefaultAsync<User>("SELECT * FROM Users WHERE Username = @Username", new { Username = username });
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            return await Connection.QueryFirstOrDefaultAsync<User>("SELECT * FROM Users WHERE Email = @Email", new { Email = email });
        }

        public async Task<User?> GetByUsernameWithRolesAsync(string username)
        {
            var sql = @"
                SELECT u.*, r.Id as RoleId, r.Name as RoleName, r.Description as RoleDescription
                FROM Users u
                LEFT JOIN UserRoles ur ON u.Id = ur.UserId
                LEFT JOIN Roles r ON ur.RoleId = r.Id
                WHERE u.Username = @Username";
            
            var userDict = new Dictionary<int, User>();
            await Connection.QueryAsync<User, Role, User>(sql, (user, role) =>
            {
                if (!userDict.TryGetValue(user.Id, out var u))
                {
                    u = user;
                    u.UserRoles = new List<UserRole>();
                    userDict.Add(user.Id, u);
                }
                if (role != null)
                {
                    u.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, Role = role });
                }
                return u;
            }, new { Username = username }, splitOn: "RoleId");
            
            return userDict.Values.FirstOrDefault();
        }

        public async Task<IEnumerable<Role>> GetUserRolesAsync(int userId)
        {
            var sql = @"
                SELECT r.* FROM Roles r
                JOIN UserRoles ur ON r.Id = ur.RoleId
                WHERE ur.UserId = @UserId";
            return await Connection.QueryAsync<Role>(sql, new { UserId = userId });
        }

        public async Task AssignRoleAsync(int userId, int roleId)
        {
            await Connection.ExecuteAsync("INSERT INTO UserRoles (UserId, RoleId) VALUES (@UserId, @RoleId) ON CONFLICT DO NOTHING", 
                new { UserId = userId, RoleId = roleId });
        }

        public async Task RemoveRoleAsync(int userId, int roleId)
        {
            await Connection.ExecuteAsync("DELETE FROM UserRoles WHERE UserId = @UserId AND RoleId = @RoleId", 
                new { UserId = userId, RoleId = roleId });
        }
    }
}