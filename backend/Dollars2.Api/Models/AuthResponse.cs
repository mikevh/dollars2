namespace Dollars2.Api.Models;

public class AuthResponse
{
    public required string Token { get; set; }
    public required string RefreshToken { get; set; }
    public DateTime ExpiresAt { get; set; }
}
