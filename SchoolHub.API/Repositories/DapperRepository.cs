using Dapper;
using Npgsql;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace SchoolHub.API.Repositories
{
    public abstract class DapperRepository<T> : IDapperRepository<T> where T : class
    {
        protected readonly IDbConnection _dbConnection;
        protected readonly string _tableName;
        protected readonly string _idColumn;

        protected DapperRepository(IDbConnection dbConnection, string tableName, string idColumn = "Id")
        {
            _dbConnection = dbConnection;
            _tableName = tableName;
            _idColumn = idColumn;
        }

        protected IDbConnection Connection => _dbConnection;

        public virtual async Task<T?> GetByIdAsync(int id)
        {
            return await _dbConnection.QueryFirstOrDefaultAsync<T>($"SELECT * FROM {_tableName} WHERE {_idColumn} = @Id", new { Id = id });
        }

        public virtual async Task<IEnumerable<T>> GetAllAsync()
        {
            return await _dbConnection.QueryAsync<T>($"SELECT * FROM {_tableName}");
        }

        public virtual async Task<int> CreateAsync(T entity)
        {
            var columns = GetColumns(excludeId: true);
            var parameters = GetParameters(columns);
            var sql = $"INSERT INTO {_tableName} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", parameters)}) RETURNING {_idColumn}";
            return await _dbConnection.ExecuteScalarAsync<int>(sql, entity);
        }

        public virtual async Task<bool> UpdateAsync(T entity)
        {
            var columns = GetColumns(excludeId: true);
            var setClause = string.Join(", ", columns.Select(c => $"{c} = @{c}"));
            var sql = $"UPDATE {_tableName} SET {setClause} WHERE {_idColumn} = @{_idColumn}";
            var rows = await _dbConnection.ExecuteAsync(sql, entity);
            return rows > 0;
        }

        public virtual async Task<bool> DeleteAsync(int id)
        {
            var sql = $"DELETE FROM {_tableName} WHERE {_idColumn} = @Id";
            var rows = await _dbConnection.ExecuteAsync(sql, new { Id = id });
            return rows > 0;
        }

        public virtual async Task<bool> ExistsAsync(int id)
        {
            var count = await _dbConnection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM {_tableName} WHERE {_idColumn} = @Id", new { Id = id });
            return count > 0;
        }

        protected virtual IEnumerable<string> GetColumns(bool excludeId = false)
        {
            var props = typeof(T).GetProperties()
                .Where(p => !excludeId || p.Name != _idColumn)
                .Select(p => p.Name);
            return props;
        }

        protected virtual IEnumerable<string> GetParameters(IEnumerable<string> columns)
        {
            return columns.Select(c => "@" + c);
        }
    }
}