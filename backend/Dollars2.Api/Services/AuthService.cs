using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
    private readonly IConfiguration _config;

    public AuthService(DbSession dbSession, UserRepository userRepo, RefreshTokenRepository refreshTokenRepo, IConfiguration config)
    {
        _dbSession = dbSession;
        _userRepo = userRepo;
        _refreshTokenRepo = refreshTokenRepo;
        _config = config;
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
        var jwt = GenerateJwt(user);
        var refreshToken = GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddDays(_config.GetValue<int>("Jwt:ExpirationDays"));

        await _refreshTokenRepo.CreateAsync(user.Id, refreshToken, expiresAt.AddDays(30));

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
