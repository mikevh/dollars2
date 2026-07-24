using System.Text.Json;

namespace Dollars2.Api.Json;

/// <summary>
/// The API's JSON conventions, in one place so tests can serialize exactly the way the API does
/// rather than against a hand-rolled copy of its settings.
/// </summary>
public static class Dollars2JsonOptions
{
    /// <summary>Applies the API's conventions to an existing options instance (used by MVC at startup).</summary>
    public static void Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new UtcDateTimeConverter());
    }

    /// <summary>
    /// A standalone options instance matching what the API serializes responses with:
    /// ASP.NET's web defaults (camelCase) plus our conventions.
    /// </summary>
    public static JsonSerializerOptions CreateWebOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }
}
