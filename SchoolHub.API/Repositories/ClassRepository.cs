using Dapper;
using Npgsql;
using SchoolHub.API.Models.Academic;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace SchoolHub.API.Repositories
{
    public class ClassRepository : DapperRepository<Class>, IClassRepository
    {
        public ClassRepository(IDbConnection dbConnection) : base(dbConnection, "Classes", "Id") { }

        public async Task<IEnumerable<Class>> GetAllWithSectionsAsync()
        {
            var sql = @"
                SELECT c.*, s.Id as SectionId, s.Name as SectionName, s.ClassId as SectionClassId
                FROM Classes c
                LEFT JOIN Sections s ON c.Id = s.ClassId
                ORDER BY c.Name";
            
            var classDict = new Dictionary<int, Class>();
            await Connection.QueryAsync<Class, Section, Class>(sql, (c, s) =>
            {
                if (!classDict.TryGetValue(c.Id, out var cls))
                {
                    cls = c;
                    cls.Sections = new List<Section>();
                    classDict.Add(cls.Id, cls);
                }
                if (s != null)
                {
                    cls.Sections.Add(s);
                }
                return cls;
            }, splitOn: "SectionId");
            
            return classDict.Values;
        }
    }
}