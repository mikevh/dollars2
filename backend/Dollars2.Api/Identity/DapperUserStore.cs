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

    public void Dispose()
    {
    }

    public Task<string> GetUserIdAsync(User user, CancellationToken cancellationToken)
        => Task.FromResult(user.Id.ToString());

    public Task<string?> GetUserNameAsync(User user, CancellationToken cancellationToken)
        => Task.FromResult<string?>(user.Email);

    public Task SetUserNameAsync(User user, string? userName, CancellationToken cancellationToken)
    {
        user.Email = userName ?? user.Email;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(User user, CancellationToken cancellationToken)
        => Task.FromResult<string?>(user.Email.ToUpperInvariant());

    public Task SetNormalizedUserNameAsync(User user, string? normalizedName, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public async Task<IdentityResult> CreateAsync(User user, CancellationToken cancellationToken)
    {
        if (await _users.GetByEmailAsync(user.Email) is not null)
        {
            return IdentityResult.Failed(DuplicateEmailError(user.Email));
        }

        user.Id = await _users.CreateAsync(user.Email);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(User user, CancellationToken cancellationToken)
    {
        var existing = await _users.GetByEmailAsync(user.Email);
        if (existing is not null && existing.Id != user.Id)
        {
            return IdentityResult.Failed(DuplicateEmailError(user.Email));
        }

        await _users.UpdateEmailAsync(user.Id, user.Email);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(User user, CancellationToken cancellationToken)
    {
        // No ON DELETE CASCADE on FK_PasskeyCredentials_Users — clear the user's credentials first
        // so the delete doesn't fail on the foreign key.
        await _passkeys.DeleteAllForUserAsync(user.Id);
        await _users.DeleteAsync(user.Id);
        return IdentityResult.Success;
    }

    private static IdentityError DuplicateEmailError(string email) => new()
    {
        Code = "DuplicateEmail",
        Description = $"Email '{email}' is already in use.",
    };

    public async Task<User?> FindByIdAsync(string userId, CancellationToken cancellationToken)
        => int.TryParse(userId, out var id) ? await _users.GetByIdAsync(id) : null;

    public async Task<User?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
        => await _users.GetByEmailAsync(normalizedUserName);

    public async Task AddOrUpdatePasskeyAsync(User user, UserPasskeyInfo passkey, CancellationToken cancellationToken)
    {
        await _passkeys.UpsertAsync(new PasskeyCredential
        {
            UserId = user.Id,
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

    public async Task<User?> FindByPasskeyIdAsync(byte[] credentialId, CancellationToken cancellationToken)
    {
        var credential = await _passkeys.FindByCredentialIdAsync(credentialId);
        return credential is null ? null : await _users.GetByIdAsync(credential.UserId);
    }

    public async Task<UserPasskeyInfo?> FindPasskeyAsync(User user, byte[] credentialId, CancellationToken cancellationToken)
    {
        var credential = await _passkeys.FindByUserAndCredentialIdAsync(user.Id, credentialId);
        return credential is null ? null : ToPasskeyInfo(credential);
    }

    public async Task<IList<UserPasskeyInfo>> GetPasskeysAsync(User user, CancellationToken cancellationToken)
    {
        var credentials = await _passkeys.GetByUserIdAsync(user.Id);
        return credentials.Select(ToPasskeyInfo).ToList();
    }

    public async Task RemovePasskeyAsync(User user, byte[] credentialId, CancellationToken cancellationToken)
        => await _passkeys.DeleteAsync(user.Id, credentialId);

    private static UserPasskeyInfo ToPasskeyInfo(PasskeyCredential credential)
    {
        var transports = credential.Transports is { Length: > 0 }
            ? credential.Transports.Split(',')
            : [];

        return new UserPasskeyInfo(
            credentialId: credential.CredentialId,
            publicKey: credential.PublicKey,
            createdAt: new DateTimeOffset(credential.CreatedAt, TimeSpan.Zero),
            signCount: (uint)credential.SignCount,
            transports: transports,
            isUserVerified: credential.IsUserVerified,
            isBackupEligible: credential.IsBackupEligible,
            isBackedUp: credential.IsBackedUp,
            attestationObject: credential.AttestationObject,
            clientDataJson: credential.ClientDataJson)
        {
            Name = credential.Name,
        };
    }
}
