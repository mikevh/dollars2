using Dollars2.Api.Models;
using Dollars2.Api.Repositories;
using Microsoft.AspNetCore.Identity;

namespace Dollars2.Api.Identity;

/// <summary>
/// Backs ASP.NET Core Identity's passkey ceremony (<see cref="IUserPasskeyStore{TUser}"/>) with the
/// app's existing Dapper/raw-SQL data access instead of EF Core. Identity is used only for the
/// WebAuthn attestation/assertion flow here — this app's own JWT + refresh token issuance remains
/// the authentication mechanism (see issue #271/#272).
/// </summary>
public class DapperUserStore : IUserPasskeyStore<User>
{
    private readonly UserRepository _users;
    private readonly PasskeyCredentialRepository _passkeys;

    public DapperUserStore(UserRepository users, PasskeyCredentialRepository passkeys)
    {
        _users = users;
        _passkeys = passkeys;
    }

    public Task<string> GetUserIdAsync(User u, CancellationToken ct) => Task.FromResult(u.Id.ToString());

    public Task<string?> GetUserNameAsync(User u, CancellationToken ct) => Task.FromResult<string?>(u.Email);

    public Task SetUserNameAsync(User u, string? userName, CancellationToken ct)
    {
        u.Email = userName ?? u.Email;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(User u, CancellationToken ct) => Task.FromResult<string?>(u.Email.ToUpperInvariant());

    public Task SetNormalizedUserNameAsync(User u, string? normalizedName, CancellationToken ct) => Task.CompletedTask;

    public async Task<IdentityResult> CreateAsync(User u, CancellationToken ct)
    {
        if (await _users.GetByEmailAsync(u.Email) is not null)
        {
            return IdentityResult.Failed(DuplicateEmailError(u.Email));
        }

        u.Id = await _users.CreateAsync(u.Email);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(User u, CancellationToken ct)
    {
        var existing = await _users.GetByEmailAsync(u.Email);
        if (existing is not null && existing.Id != u.Id)
        {
            return IdentityResult.Failed(DuplicateEmailError(u.Email));
        }

        await _users.UpdateEmailAsync(u.Id, u.Email);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(User u, CancellationToken ct)
    {
        // No ON DELETE CASCADE on FK_PasskeyCredentials_Users — clear the user's credentials first
        // so the delete doesn't fail on the foreign key.
        await _passkeys.DeleteAllForUserAsync(u.Id);
        await _users.DeleteAsync(u.Id);
        return IdentityResult.Success;
    }

    private static IdentityError DuplicateEmailError(string email) => new()
    {
        Code = "DuplicateEmail",
        Description = $"Email '{email}' is already in use.",
    };

    public async Task<User?> FindByIdAsync(string userId, CancellationToken ct) => int.TryParse(userId, out var id) ? await _users.GetByIdAsync(id) : null;

    public async Task<User?> FindByNameAsync(string normalizedUserName, CancellationToken ct) => await _users.GetByEmailAsync(normalizedUserName);

    public async Task AddOrUpdatePasskeyAsync(User u, UserPasskeyInfo passkey, CancellationToken ct)
    {
        await _passkeys.UpsertAsync(new PasskeyCredential
        {
            UserId = u.Id,
            CredentialId = passkey.CredentialId,
            PublicKey = passkey.PublicKey,
            AttestationObject = passkey.AttestationObject,
            ClientDataJson = passkey.ClientDataJson,
            SignCount = passkey.SignCount,
            Transports = passkey.Transports is { Length: > 0 } transports ? string.Join(',', transports) : null,
            IsUserVerified = passkey.IsUserVerified,
            IsBackupEligible = passkey.IsBackupEligible,
            IsBackedUp = passkey.IsBackedUp,
            Name = passkey.Name,
            CreatedAt = passkey.CreatedAt.UtcDateTime,
        });
    }

    public async Task<User?> FindByPasskeyIdAsync(byte[] credentialId, CancellationToken ct)
    {
        var cred = await _passkeys.FindByCredentialIdAsync(credentialId);
        return cred is null ? null : await _users.GetByIdAsync(cred.UserId);
    }

    public async Task<UserPasskeyInfo?> FindPasskeyAsync(User u, byte[] credentialId, CancellationToken ct)
    {
        var cred = await _passkeys.FindByUserAndCredentialIdAsync(u.Id, credentialId);
        return cred is null ? null : ToPasskeyInfo(cred);
    }

    public async Task<IList<UserPasskeyInfo>> GetPasskeysAsync(User u, CancellationToken ct)
    {
        var creds = await _passkeys.GetByUserIdAsync(u.Id);
        return creds.Select(ToPasskeyInfo).ToList();
    }

    public async Task RemovePasskeyAsync(User u, byte[] credId, CancellationToken ct) => await _passkeys.DeleteAsync(u.Id, credId);

    private static UserPasskeyInfo ToPasskeyInfo(PasskeyCredential cred)
    {
        var transports = cred.Transports is { Length: > 0 }
            ? cred.Transports.Split(',')
            : [];

        return new UserPasskeyInfo(
            credentialId: cred.CredentialId,
            publicKey: cred.PublicKey,
            createdAt: new DateTimeOffset(cred.CreatedAt, TimeSpan.Zero),
            signCount: (uint)cred.SignCount,
            transports: transports,
            isUserVerified: cred.IsUserVerified,
            isBackupEligible: cred.IsBackupEligible,
            isBackedUp: cred.IsBackedUp,
            attestationObject: cred.AttestationObject,
            clientDataJson: cred.ClientDataJson)
        {
            Name = cred.Name,
        };
    }

    public void Dispose() { }
}
