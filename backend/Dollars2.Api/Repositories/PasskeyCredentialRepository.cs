using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Models;

namespace Dollars2.Api.Repositories;

public class PasskeyCredentialRepository
{
    private const string SelectColumns = "Id, UserId, CredentialId, PublicKey, AttestationObject, ClientDataJson, SignCount, Transports, IsUserVerified, IsBackupEligible, IsBackedUp, Name, CreatedAt, UpdatedAt";

    private readonly DbSession _db;

    public PasskeyCredentialRepository(DbSession db)
    {
        _db = db;
    }

    public async Task<PasskeyCredential?> FindByUserAndCredentialIdAsync(int userId, byte[] credentialId)
    {
        var sql = $"SELECT {SelectColumns} FROM PasskeyCredentials WHERE UserId = @userId AND CredentialId = @credentialId";
        var rv = await _db.Connection.QuerySingleOrDefaultAsync<PasskeyCredential>(sql, new { userId, credentialId }, _db.CurrentTransaction);

        return rv;
    }

    public async Task<IEnumerable<PasskeyCredential>> GetByUserIdAsync(int userId)
    {
        var sql = $"SELECT {SelectColumns} FROM PasskeyCredentials WHERE UserId = @userId";
        var rv = await _db.Connection.QueryAsync<PasskeyCredential>(sql, new { userId }, _db.CurrentTransaction);

        return rv;
    }

    /// <summary>
    /// Inserts a new credential, or updates the mutable fields (sign count, verification/backup
    /// flags, friendly name) of an existing one matched by CredentialId. A single atomic MERGE
    /// rather than a separate exists-check + insert/update — the latter is a check-then-act race
    /// between two concurrent upserts of the same brand-new CredentialId. HOLDLOCK takes a
    /// serializable-equivalent lock on the matched range for the duration of the statement, so a
    /// concurrent MERGE on the same CredentialId blocks instead of both taking the insert branch.
    /// </summary>
    public async Task UpsertAsync(PasskeyCredential credential)
    {
        await _db.Connection.ExecuteAsync(
            """
            MERGE PasskeyCredentials WITH (HOLDLOCK) AS target
            USING (SELECT @CredentialId AS CredentialId) AS source
            ON target.CredentialId = source.CredentialId
            WHEN MATCHED THEN
                UPDATE SET
                    SignCount = @SignCount,
                    IsUserVerified = @IsUserVerified,
                    IsBackupEligible = @IsBackupEligible,
                    IsBackedUp = @IsBackedUp,
                    Name = @Name,
                    UpdatedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (UserId, CredentialId, PublicKey, AttestationObject, ClientDataJson, SignCount, Transports, IsUserVerified, IsBackupEligible, IsBackedUp, Name, CreatedAt, UpdatedAt)
                VALUES (@UserId, @CredentialId, @PublicKey, @AttestationObject, @ClientDataJson, @SignCount, @Transports, @IsUserVerified, @IsBackupEligible, @IsBackedUp, @Name, @CreatedAt, SYSUTCDATETIME());
            """,
            credential,
            _db.CurrentTransaction);
    }

    public async Task DeleteAsync(int userId, byte[] credentialId)
    {
        var sql = "DELETE FROM PasskeyCredentials WHERE UserId = @userId AND CredentialId = @credentialId";
        await _db.Connection.ExecuteAsync(sql, new { userId, credentialId }, _db.CurrentTransaction);
    }

    /// <summary>
    /// Removes every credential for a user — used both when deleting the user (IUserStore.DeleteAsync
    /// has no ON DELETE CASCADE to rely on) and for lost-passkey recovery, where re-registering
    /// revokes whatever credentials existed before (see issue #271/#272).
    /// </summary>
    public async Task<int> DeleteAllForUserAsync(int userId)
    {
        var sql = "DELETE FROM PasskeyCredentials WHERE UserId = @userId";
        var rv = await _db.Connection.ExecuteAsync(sql, new { userId }, _db.CurrentTransaction);

        return rv;
    }
}
