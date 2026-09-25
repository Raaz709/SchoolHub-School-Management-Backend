using Dapper;
using Npgsql;
using SchoolHub.API.Models.People;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace SchoolHub.API.Repositories
{
    public class TeacherRepository : DapperRepository<Teacher>, ITeacherRepository
    {
        public TeacherRepository(IDbConnection dbConnection) : base(dbConnection, "Teachers", "Id") { }

        public async Task<Teacher?> GetByUserIdAsync(int userId)
        {
            return await Connection.QueryFirstOrDefaultAsync<Teacher>("SELECT * FROM Teachers WHERE UserId = @UserId", new { UserId = userId });
        }

        public async Task<Teacher?> GetByEmployeeCodeAsync(string employeeCode)
        {
            return await Connection.QueryFirstOrDefaultAsync<Teacher>("SELECT * FROM Teachers WHERE EmployeeCode = @EmployeeCode", new { EmployeeCode = employeeCode });
        }

        public async Task<IEnumerable<Teacher>> GetByDepartmentAsync(int departmentId)
        {
            return await Connection.QueryAsync<Teacher>("SELECT * FROM Teachers WHERE DepartmentId = @DepartmentId", new { DepartmentId = departmentId });
        }
    }
}