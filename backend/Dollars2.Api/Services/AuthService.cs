using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dollars2.Api.Configuration;
using Dollars2.Api.Data;
using Dollars2.Api.Models;
using Dollars2.Api.Repositories;
using Microsoft.IdentityModel.Tokens;

namespace Dollars2.Api.Services;

public class AuthService
{
    private readonly DbSession _dbSession;
    private readonly UserRepository _userRepo;
    private readonly RefreshTokenRepository _refreshTokenRepo;
    private readonly JwtSettings _jwtSettings;

    public AuthService(DbSession dbSession, UserRepository userRepo, RefreshTokenRepository refreshTokenRepo, JwtSettings jwtSettings)
    {
        _dbSession = dbSession;
        _userRepo = userRepo;
        _refreshTokenRepo = refreshTokenRepo;
        _jwtSettings = jwtSettings;
    }

    public async Task<DollarsApiResponse<AuthResponse>> LoginAsync(string email)
    {
        var user = await _userRepo.GetByEmailAsync(email);

        if (user is null)
        {
            return DollarsApiResponse<AuthResponse>.Fail("User not found.", "USER_NOT_FOUND");
        }

        return await GenerateTokensAsync(user);
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
        var now = DateTime.UtcNow;
        // One expiry instant drives both the token's `exp` claim and the ExpiresAt we hand the
        // client, so the two can never disagree about when the session dies.
        var expiresAt = now.AddDays(_jwtSettings.ExpirationDays);
        var jwt = GenerateJwt(user, expiresAt);
        var refreshToken = GenerateRefreshToken();

        await _refreshTokenRepo.CreateAsync(user.Id, refreshToken, now.AddDays(_jwtSettings.RefreshExpirationDays));

        return DollarsApiResponse<AuthResponse>.Success(new AuthResponse
        {
            Token = jwt,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt
        });
    }

    private string GenerateJwt(User user, DateTime expiresAt)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }
}
