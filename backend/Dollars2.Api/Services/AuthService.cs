using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dollars2.Api.Data;
using Dollars2.Api.Models;
using Dollars2.Api.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
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
    private readonly IHostEnvironment _env;

    public AuthService(
        DbSession dbSession,
        UserRepository userRepo,
        RefreshTokenRepository refreshTokenRepo,
        PasskeyCredentialRepository passkeyRepo,
        IUserPasskeyStore<User> passkeyStore,
        IPasskeyHandler<User> passkeyHandler,
        IConfiguration config,
        IHostEnvironment env)
    {
        _dbSession = dbSession;
        _userRepo = userRepo;
        _refreshTokenRepo = refreshTokenRepo;
        _passkeyRepo = passkeyRepo;
        _passkeyStore = passkeyStore;
        _passkeyHandler = passkeyHandler;
        _config = config;
        _env = env;
    }

    public async Task<(DollarsApiResponse<PasskeyOptionsResponse> Response, string? AttestationState)> GetPasskeyRegistrationOptionsAsync(string email, string registrationKey, HttpContext httpContext)
    {
        var user = await _userRepo.GetByEmailAsync(email);

        if (user is null || !SecureEquals(user.RegistrationKey, registrationKey))
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

    public async Task<DollarsApiResponse<object>> CompletePasskeyRegistrationAsync(string credentialJson, string? expectedRegistrationKey, string? attestationState, HttpContext httpContext)
    {
        if (attestationState is null || expectedRegistrationKey is null)
        {
            return DollarsApiResponse<object>.Fail("Registration ceremony expired or missing.", "CEREMONY_STATE_MISSING");
        }

        var attestation = await _passkeyHandler.PerformAttestationAsync(new PasskeyAttestationContext
        {
            HttpContext = httpContext,
            CredentialJson = credentialJson,
            AttestationState = attestationState,
        });

        if (!attestation.Succeeded || attestation.UserEntity is null)
        {
            return DollarsApiResponse<object>.Fail(attestation.Failure?.Message ?? "Passkey registration failed.", "PASSKEY_ATTESTATION_FAILED");
        }

        if (!int.TryParse(attestation.UserEntity.Id, out var userId))
        {
            return DollarsApiResponse<object>.Fail("Passkey registration failed.", "PASSKEY_ATTESTATION_FAILED");
        }

        var user = await _userRepo.GetByIdAsync(userId);

        // Defense in depth: re-validates against the exact key bound to this ceremony at the
        // options step (not just "some key is set") — closes the window where the key was
        // rotated to a new value mid-ceremony, which would otherwise let a stale ceremony
        // complete under credentials issued for a key that's no longer current.
        if (user is null || !SecureEquals(user.RegistrationKey, expectedRegistrationKey))
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

    // Local-dev-only bypass so the passkey ceremony (which is bound to a specific rp.id) doesn't
    // have to be re-registered every time someone points a local `dotnet run` at the real prod
    // database to debug. Gated on IsDevelopment() rather than a config flag so there is nothing to
    // forget to unset: launchSettings.json sets ASPNETCORE_ENVIRONMENT=Development for local runs,
    // and docker-compose.yml hardcodes it to Production on claw, so this can't reach a real
    // deployment. Checked here too, not just at the controller, in case a future caller reuses
    // this method without going through that route.
    public async Task<DollarsApiResponse<AuthResponse>> CompleteDevLoginAsync(string email)
    {
        if (!_env.IsDevelopment())
        {
            return DollarsApiResponse<AuthResponse>.Fail("Dev login is not available.", "DEV_LOGIN_DISABLED");
        }

        var user = await _userRepo.GetByEmailAsync(email);

        if (user is null)
        {
            return DollarsApiResponse<AuthResponse>.Fail("User not found.", "USER_NOT_FOUND");
        }

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

    // The registration key is a secret, single-use credential — a plain string compare would let
    // an attacker who can measure response timing brute-force it one character at a time.
    private static bool SecureEquals(string? actual, string? candidate)
    {
        if (actual is null || candidate is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(candidate));
    }
}
