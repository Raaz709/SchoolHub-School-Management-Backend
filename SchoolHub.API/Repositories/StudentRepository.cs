using Dapper;
using Npgsql;
using SchoolHub.API.Models.People;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace SchoolHub.API.Repositories
{
    public class StudentRepository : DapperRepository<Student>, IStudentRepository
    {
        public StudentRepository(IDbConnection dbConnection) : base(dbConnection, "Students", "Id") { }

        public async Task<Student?> GetByUserIdAsync(int userId)
        {
            return await Connection.QueryFirstOrDefaultAsync<Student>("SELECT * FROM Students WHERE UserId = @UserId", new { UserId = userId });
        }

        public async Task<Student?> GetByRollNumberAsync(string rollNumber)
        {
            return await Connection.QueryFirstOrDefaultAsync<Student>("SELECT * FROM Students WHERE RollNumber = @RollNumber", new { RollNumber = rollNumber });
        }

        public async Task<IEnumerable<Student>> GetByClassAsync(int classId)
        {
            var sql = @"
                SELECT s.* FROM Students s
                JOIN Enrollments e ON s.Id = e.StudentId
                WHERE e.ClassId = @ClassId";
            return await Connection.QueryAsync<Student>(sql, new { ClassId = classId });
        }

        public async Task<IEnumerable<Student>> GetBySectionAsync(int sectionId)
        {
            var sql = @"
                SELECT s.* FROM Students s
                JOIN Enrollments e ON s.Id = e.StudentId
                WHERE e.SectionId = @SectionId";
            return await Connection.QueryAsync<Student>(sql, new { SectionId = sectionId });
        }
    }
}