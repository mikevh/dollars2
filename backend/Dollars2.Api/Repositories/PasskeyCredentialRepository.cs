using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Models;

namespace Dollars2.Api.Repositories;

public class PasskeyCredentialRepository
{
    private const string SelectColumns =
        "Id, UserId, CredentialId, PublicKey, AttestationObject, ClientDataJson, SignCount, Transports, IsUserVerified, IsBackupEligible, IsBackedUp, Name, CreatedAt, UpdatedAt";

    private readonly DbSession _db;

    public PasskeyCredentialRepository(DbSession db)
    {
        _db = db;
    }

    public async Task<PasskeyCredential?> FindByCredentialIdAsync(byte[] credentialId)
    {
        return await _db.Connection.QuerySingleOrDefaultAsync<PasskeyCredential>(
            $"SELECT {SelectColumns} FROM PasskeyCredentials WHERE CredentialId = @credentialId",
            new { credentialId },
            _db.CurrentTransaction);
    }

    public async Task<PasskeyCredential?> FindByUserAndCredentialIdAsync(int userId, byte[] credentialId)
    {
        return await _db.Connection.QuerySingleOrDefaultAsync<PasskeyCredential>(
            $"SELECT {SelectColumns} FROM PasskeyCredentials WHERE UserId = @userId AND CredentialId = @credentialId",
            new { userId, credentialId },
            _db.CurrentTransaction);
    }

    public async Task<IEnumerable<PasskeyCredential>> GetByUserIdAsync(int userId)
    {
        return await _db.Connection.QueryAsync<PasskeyCredential>(
            $"SELECT {SelectColumns} FROM PasskeyCredentials WHERE UserId = @userId",
            new { userId },
            _db.CurrentTransaction);
    }

    /// <summary>
    /// Inserts a new credential, or updates the mutable fields (sign count, verification/backup
    /// flags, friendly name) of an existing one matched by CredentialId.
    /// </summary>
    public async Task UpsertAsync(PasskeyCredential credential)
    {
        await _db.Connection.ExecuteAsync(
            """
            IF EXISTS (SELECT 1 FROM PasskeyCredentials WHERE CredentialId = @CredentialId)
            BEGIN
                UPDATE PasskeyCredentials
                SET SignCount = @SignCount,
                    IsUserVerified = @IsUserVerified,
                    IsBackupEligible = @IsBackupEligible,
                    IsBackedUp = @IsBackedUp,
                    Name = @Name,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE CredentialId = @CredentialId;
            END
            ELSE
            BEGIN
                INSERT INTO PasskeyCredentials
                    (UserId, CredentialId, PublicKey, AttestationObject, ClientDataJson, SignCount, Transports, IsUserVerified, IsBackupEligible, IsBackedUp, Name, CreatedAt, UpdatedAt)
                VALUES
                    (@UserId, @CredentialId, @PublicKey, @AttestationObject, @ClientDataJson, @SignCount, @Transports, @IsUserVerified, @IsBackupEligible, @IsBackedUp, @Name, @CreatedAt, SYSUTCDATETIME());
            END
            """,
            credential,
            _db.CurrentTransaction);
    }

    public async Task DeleteAsync(int userId, byte[] credentialId)
    {
        await _db.Connection.ExecuteAsync(
            "DELETE FROM PasskeyCredentials WHERE UserId = @userId AND CredentialId = @credentialId",
            new { userId, credentialId },
            _db.CurrentTransaction);
    }
}
