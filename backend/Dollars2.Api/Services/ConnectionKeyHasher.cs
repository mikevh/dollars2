using System.Security.Cryptography;
using System.Text;

namespace Dollars2.Api.Services;

/// <summary>
/// Derives the stable, opaque connection id exposed to clients from a provider's raw connection key.
/// The raw key holds secrets (a Plaid access token, or a SimpleFIN access URL + username), so it must
/// never leave the server. A one-way hash gives a stable identity without exposing anything sensitive.
/// Shared so the emit side (GET /api/accounts) and the resolve side (per-group sync) produce identical ids.
/// </summary>
public static class ConnectionKeyHasher
{
    public static string Hash(string sourceType, string connectionKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceType}\n{connectionKey}"));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
