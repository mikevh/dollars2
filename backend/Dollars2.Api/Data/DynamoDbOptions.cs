namespace Dollars2.Api.Data;

/// <summary>
/// Configuration for the sync archive's DynamoDB store — the append-only record of the raw JSON
/// returned by the bank sync providers.
/// </summary>
/// <remarks>
/// This is always dynamodb-local (the <c>amazon/dynamodb-local</c> container), in development and in
/// production alike. There is no AWS account behind it, so there is deliberately no credential,
/// region, or profile setting here: the endpoint and the table name are the whole configuration
/// surface. See the DI registration in <c>Program.cs</c> for why the SDK still needs throwaway
/// credentials and why they are hardcoded rather than configured.
/// </remarks>
public class DynamoDbOptions
{
    public const string SectionName = "DynamoDb";

    /// <summary>
    /// Endpoint of the dynamodb-local instance. Defaults to the local-development value, where the
    /// backend runs on the host via <c>dotnet run</c> and reaches the container's published port.
    /// docker-compose overrides this with the in-network address (<c>http://dynamodb:8000</c>).
    /// </summary>
    public string ServiceUrl { get; set; } = "http://localhost:8000";

    /// <summary>
    /// Name of the single table holding every archived payload.
    /// </summary>
    public string TableName { get; set; } = "Dollars2SyncArchive";
}
