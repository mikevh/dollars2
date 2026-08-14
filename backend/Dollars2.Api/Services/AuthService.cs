using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dollars2.Api.Data;
using Dollars2.Api.Models;
using Dollars2.Api.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace Dollars2.Api.Services;

public class AuthService
{
    private readonly DbSession _dbSession;
    private readonly UserRepository _userRepo;
    private readonly RefreshTokenRepository _refreshTokenRepo;
    private readonly PasskeyCredentialRepository _passkeyRepo;
    private readonly IUserPasskeyStore<User> _passkeyStore;
    private readonly IPasskeyHandler<User> _passkeyHandler;
    private readonly IConfiguration _config;

    public AuthService(
        DbSession dbSession,
        UserRepository userRepo,
        RefreshTokenRepository refreshTokenRepo,
        PasskeyCredentialRepository passkeyRepo,
        IUserPasskeyStore<User> passkeyStore,
        IPasskeyHandler<User> passkeyHandler,
        IConfiguration config)
    {
        _dbSession = dbSession;
        _userRepo = userRepo;
        _refreshTokenRepo = refreshTokenRepo;
        _passkeyRepo = passkeyRepo;
        _passkeyStore = passkeyStore;
        _passkeyHandler = passkeyHandler;
        _config = config;
    }

    public async Task<(DollarsApiResponse<PasskeyOptionsResponse> Response, string? AttestationState)> GetPasskeyRegistrationOptionsAsync(string email, string registrationKey, HttpContext httpContext)
    {
        var user = await _userRepo.GetByEmailAsync(email);

        if (user is null || user.RegistrationKey is null || user.RegistrationKey != registrationKey)
        {
            return (DollarsApiResponse<PasskeyOptionsResponse>.Fail("Invalid registration key.", "INVALID_REGISTRATION_KEY"), null);
        }

        var userEntity = new PasskeyUserEntity
        {
            Id = user.Id.ToString(),
            Name = user.Email,
            DisplayName = user.Email,
        };

        var options = await _passkeyHandler.MakeCreationOptionsAsync(userEntity, httpContext);
        var response = DollarsApiResponse<PasskeyOptionsResponse>.Success(new PasskeyOptionsResponse { OptionsJson = options.CreationOptionsJson });
        return (response, options.AttestationState);
    }

    public async Task<DollarsApiResponse<object>> CompletePasskeyRegistrationAsync(string credentialJson, string? attestationState, HttpContext httpContext)
    {
        if (attestationState is null)
        {
            return DollarsApiResponse<object>.Fail("Registration ceremony expired or missing.", "CEREMONY_STATE_MISSING");
        }

        var attestation = await _passkeyHandler.PerformAttestationAsync(new PasskeyAttestationContext
        {
            HttpContext = httpContext,
            CredentialJson = credentialJson,
            AttestationState = attestationState,
        });

        if (!attestation.Succeeded)
        {
            return DollarsApiResponse<object>.Fail(attestation.Failure?.Message ?? "Passkey registration failed.", "PASSKEY_ATTESTATION_FAILED");
        }

        if (!int.TryParse(attestation.UserEntity.Id, out var userId))
        {
            return DollarsApiResponse<object>.Fail("Passkey registration failed.", "PASSKEY_ATTESTATION_FAILED");
        }

        var user = await _userRepo.GetByIdAsync(userId);

        // Defense in depth: the registration key must still be set at completion time, even though
        // the options step already validated it — closes the window where it was cleared or changed
        // mid-ceremony.
        if (user is null || user.RegistrationKey is null)
        {
            return DollarsApiResponse<object>.Fail("Registration key is no longer valid.", "INVALID_REGISTRATION_KEY");
        }

        _dbSession.BeginTransaction();
        try
        {
            // Lost-passkey recovery presumes the old credential is compromised — re-registering
            // revokes whatever credentials existed before.
            await _passkeyRepo.DeleteAllForUserAsync(user.Id);
            await _passkeyStore.AddOrUpdatePasskeyAsync(user, attestation.Passkey, CancellationToken.None);
            await _userRepo.ClearRegistrationKeyAsync(user.Id);
            _dbSession.Commit();
        }
        catch
        {
            _dbSession.Rollback();
            throw;
        }

        return DollarsApiResponse<object>.Success(new { });
    }

    public async Task<(DollarsApiResponse<PasskeyOptionsResponse> Response, string? AssertionState)> GetPasskeyLoginOptionsAsync(string email, HttpContext httpContext)
    {
        var user = await _userRepo.GetByEmailAsync(email);

        if (user is null)
        {
            return (DollarsApiResponse<PasskeyOptionsResponse>.Fail("User not found.", "USER_NOT_FOUND"), null);
        }

        var options = await _passkeyHandler.MakeRequestOptionsAsync(user, httpContext);
        var response = DollarsApiResponse<PasskeyOptionsResponse>.Success(new PasskeyOptionsResponse { OptionsJson = options.RequestOptionsJson });
        return (response, options.AssertionState);
    }

    public async Task<DollarsApiResponse<AuthResponse>> CompletePasskeyLoginAsync(string credentialJson, string? assertionState, HttpContext httpContext)
    {
        if (assertionState is null)
        {
            return DollarsApiResponse<AuthResponse>.Fail("Login ceremony expired or missing.", "CEREMONY_STATE_MISSING");
        }

        var assertion = await _passkeyHandler.PerformAssertionAsync(new PasskeyAssertionContext
        {
            HttpContext = httpContext,
            CredentialJson = credentialJson,
            AssertionState = assertionState,
        });

        if (!assertion.Succeeded || assertion.User is null)
        {
            return DollarsApiResponse<AuthResponse>.Fail(assertion.Failure?.Message ?? "Passkey login failed.", "PASSKEY_ASSERTION_FAILED");
        }

        var user = assertion.User;

        _dbSession.BeginTransaction();
        try
        {
            await _refreshTokenRepo.DeleteExpiredForUserAsync(user.Id);
            var result = await GenerateTokensAsync(user);
            _dbSession.Commit();
            return result;
        }
        catch
        {
            _dbSession.Rollback();
            throw;
        }
    }

    public async Task<DollarsApiResponse<AuthResponse>> RefreshAsync(string refreshToken)
    {
        var token = await _refreshTokenRepo.GetValidTokenAsync(refreshToken);

        if (token is null)
        {
            return DollarsApiResponse<AuthResponse>.Fail("Invalid or expired refresh token.", "INVALID_REFRESH_TOKEN");
        }

        var user = await _userRepo.GetByIdAsync(token.UserId);

        if (user is null)
        {
            return DollarsApiResponse<AuthResponse>.Fail("User not found.", "USER_NOT_FOUND");
        }

        _dbSession.BeginTransaction();
        try
        {
            await _refreshTokenRepo.DeleteAsync(token.Id);
            await _refreshTokenRepo.DeleteExpiredForUserAsync(user.Id);
            var result = await GenerateTokensAsync(user);
            _dbSession.Commit();
            return result;
        }
        catch
        {
            _dbSession.Rollback();
            throw;
        }
    }

    private async Task<DollarsApiResponse<AuthResponse>> GenerateTokensAsync(User user)
    {
        var jwt = GenerateJwt(user);
        var refreshToken = GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddDays(_config.GetValue<int>("Jwt:ExpirationDays"));
        var refreshExpirationDays = _config.GetValue<int?>("Jwt:RefreshExpirationDays") ?? 30;

        await _refreshTokenRepo.CreateAsync(user.Id, refreshToken, DateTime.UtcNow.AddDays(refreshExpirationDays));

        return DollarsApiResponse<AuthResponse>.Success(new AuthResponse
        {
            Token = jwt,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt
        });
    }

    private string GenerateJwt(User user)
    {
        var secret = _config["Jwt:Secret"]!;
        var issuer = _config["Jwt:Issuer"] ?? "Dollars2";
        var audience = _config["Jwt:Audience"] ?? "Dollars2";
        var expirationDays = _config.GetValue<int>("Jwt:ExpirationDays");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(expirationDays),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }
}
