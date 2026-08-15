using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Models;

namespace Dollars2.Api.Repositories;

public class UserRepository
{
    private readonly DbSession _db;

    public UserRepository(DbSession db)
    {
        _db = db;
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        var sql = "SELECT Id, Email, RegistrationKey, CreatedAt, UpdatedAt FROM Users WHERE Email = @email";
        var rv = await _db.Connection.QuerySingleOrDefaultAsync<User>(sql, new { email }, _db.CurrentTransaction);

        return rv;
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        var sql = "SELECT Id, Email, RegistrationKey, CreatedAt, UpdatedAt FROM Users WHERE Id = @id";
        var rv = await _db.Connection.QuerySingleOrDefaultAsync<User>(sql, new { id }, _db.CurrentTransaction);

        return rv;
    }

    public async Task<User?> GetByPasskeyCredentialIdAsync(byte[] credentialId)
    {
        var sql = """
            SELECT u.Id, u.Email, u.RegistrationKey, u.CreatedAt, u.UpdatedAt
            FROM Users u
            INNER JOIN PasskeyCredentials pc ON pc.UserId = u.Id
            WHERE pc.CredentialId = @credentialId
            """;
        var rv = await _db.Connection.QuerySingleOrDefaultAsync<User>(sql, new { credentialId }, _db.CurrentTransaction);

        return rv;
    }

    public async Task<IEnumerable<int>> GetAllIdsAsync()
    {
        var rv = await _db.Connection.QueryAsync<int>("SELECT Id FROM Users", null, _db.CurrentTransaction);
        
        return rv;
    }

    public async Task<int> CreateAsync(string email)
    {
        var sql = "INSERT INTO Users (Email, CreatedAt, UpdatedAt) OUTPUT INSERTED.Id VALUES (@email, SYSUTCDATETIME(), SYSUTCDATETIME())";
        var rv = await _db.Connection.ExecuteScalarAsync<int>(sql, new { email }, _db.CurrentTransaction);

        return rv;
    }

    public async Task UpdateEmailAsync(int id, string email)
    {
        var sql = "UPDATE Users SET Email = @email, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id";
        await _db.Connection.ExecuteAsync(sql, new { id, email }, _db.CurrentTransaction);
    }

    public async Task DeleteAsync(int id)
    {
        var sql = "DELETE FROM Users WHERE Id = @id";
        await _db.Connection.ExecuteAsync(sql, new { id }, _db.CurrentTransaction);
    }

    public async Task ClearRegistrationKeyAsync(int id)
    {
        var sql = "UPDATE Users SET RegistrationKey = NULL, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id";
        await _db.Connection.ExecuteAsync(sql, new { id }, _db.CurrentTransaction);
    }
}
