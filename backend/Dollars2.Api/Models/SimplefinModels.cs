using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dollars2.Api.Models;

public class SimplefinConnectionDetails
{
    public string AccountId { get; set; } = "";
}

internal class SimplefinAccountSet
{
    // Typed as JsonElement because the SimpleFIN spec describes these as structured objects,
    // not plain strings. Using JsonElement avoids silent deserialization failure if the shape
    // differs from expectations.
    [JsonPropertyName("errlist")]
    public List<JsonElement> Errlist { get; set; } = new();

    [JsonPropertyName("accounts")]
    public List<SimplefinAccount> Accounts { get; set; } = new();
}

internal class SimplefinAccount
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("balance")]
    public string Balance { get; set; } = "0";

    [JsonPropertyName("transactions")]
    public List<SimplefinTransaction> Transactions { get; set; } = new();
}

internal class SimplefinTransaction
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("posted")]
    public long Posted { get; set; }

    [JsonPropertyName("amount")]
    public string Amount { get; set; } = "0";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("pending")]
    public bool Pending { get; set; }
}
