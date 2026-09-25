using Dapper;
using Npgsql;
using SchoolHub.API.Models.People;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace SchoolHub.API.Repositories
{
    public class ParentRepository : DapperRepository<Parent>, IParentRepository
    {
        public ParentRepository(IDbConnection dbConnection) : base(dbConnection, "Parents", "Id") { }

        public async Task<Parent?> GetByUserIdAsync(int userId)
        {
            return await Connection.QueryFirstOrDefaultAsync<Parent>("SELECT * FROM Parents WHERE UserId = @UserId", new { UserId = userId });
        }

        public async Task<IEnumerable<Student>> GetChildrenAsync(int parentId)
        {
            var sql = @"
                SELECT s.* FROM Students s
                JOIN StudentParents sp ON s.Id = sp.StudentId
                WHERE sp.ParentId = @ParentId";
            return await Connection.QueryAsync<Student>(sql, new { ParentId = parentId });
        }
    }
}