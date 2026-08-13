using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Dollars2.Api.Data;
using Dollars2.Api.Services;

namespace Dollars2.Api.Repositories;

/// <summary>
/// Writes one connection-level sync fetch's raw provider response body/bodies into the DynamoDB
/// archive table, verbatim and unparsed — one item per raw HTTP response.
/// </summary>
/// <remarks>
/// Append-only: every fetch writes new items, keyed by provider and instant, so re-fetching the same
/// window on a later sync never overwrites an earlier archived response.
///
/// Unlike every other repository here this one is not Dapper over <see cref="DbSession"/> and takes no
/// part in its transaction: DynamoDB cannot join an MSSQL transaction, and enrolling a best-effort
/// external write in it would defeat the point of it being best-effort. Callers are expected to treat
/// failures here as non-fatal.
/// </remarks>
public class SyncArchiveRepository
{
    /// <summary>The provider's response body, verbatim.</summary>
    public const string RawJsonAttribute = "rawJson";

    /// <summary>Hard limit imposed by DynamoDB on one BatchWriteItem request.</summary>
    private const int MaxBatchSize = 25;

    /// <summary>
    /// How many times a batch is sent before its leftovers are abandoned. UnprocessedItems means
    /// throttling, so retrying is the documented response — but this runs inside a sync, and an archive
    /// is not worth stalling one indefinitely.
    /// </summary>
    private const int MaxBatchWriteAttempts = 4;

    private static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// ISO-8601 UTC with an explicit Z, to millisecond precision. This string is part of the partition
    /// key, so it has to be fixed-width and lexicographically chronological.
    /// </summary>
    private const string InstantFormat = "yyyy-MM-ddTHH:mm:ss.fff'Z'";

    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly DynamoDbOptions _options;

    public SyncArchiveRepository(IAmazonDynamoDB dynamoDb, DynamoDbOptions options)
    {
        _dynamoDb = dynamoDb;
        _options = options;
    }

    /// <summary>
    /// Ceiling on one <see cref="ArchiveAsync"/> call, covering every batch it sends. The client
    /// Program.cs registers overrides neither Timeout nor MaxErrorRetry, so without a bound of its own a
    /// DynamoDB that accepts connections and then goes silent would hold up each sync for the SDK's full
    /// retry budget — minutes, for a write nobody is waiting on. Settable so tests can assert the bound
    /// without sitting through it; DI leaves it at the default.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Archives every raw response body a connection-level fetch produced. Does nothing when
    /// <paramref name="rawResponseBodies"/> is empty.
    /// </summary>
    /// <remarks>
    /// Throws on failure rather than swallowing: "best-effort" is the caller's policy to apply, and a
    /// repository that silently reported success would make an outage invisible.
    /// </remarks>
    public async Task ArchiveAsync(
        string sourceType,
        IReadOnlyList<string> rawResponseBodies,
        DateTime syncedAt,
        CancellationToken cancellationToken = default)
    {
        var items = BuildItems(sourceType, rawResponseBodies, syncedAt);
        if (items.Count == 0)
        {
            return;
        }

        // Every SDK call runs on this token, so a call that hangs is bounded too — a deadline checked
        // between batches would not be, and one wedged BatchWriteItem would sail past it.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        foreach (var chunk in items.Chunk(MaxBatchSize))
        {
            var batch = chunk
                .Select(item => new WriteRequest { PutRequest = new PutRequest { Item = item } })
                .ToList();

            await WriteBatchAsync(batch, timeout.Token);
        }
    }

    /// <summary>
    /// The items one connection-level fetch becomes: one per raw response body, keyed
    /// <c>{sourceType}#{instant}</c> when there is exactly one body, or
    /// <c>{sourceType}#{instant}#{page}</c> (zero-padded) per body when there are several — a paginated
    /// provider capturing more than one page in the same run needs the page segment to keep every item's
    /// key unique, since the table has no sort key. Pure, and public so the key construction can be
    /// asserted directly — the keys are the schema here, and they are far easier to get wrong than to
    /// check.
    /// </summary>
    public static IReadOnlyList<Dictionary<string, AttributeValue>> BuildItems(
        string sourceType,
        IReadOnlyList<string> rawResponseBodies,
        DateTime syncedAt)
    {
        var instant = FormatInstant(syncedAt);
        var items = new List<Dictionary<string, AttributeValue>>(rawResponseBodies.Count);

        for (var i = 0; i < rawResponseBodies.Count; i++)
        {
            var key = rawResponseBodies.Count == 1
                ? $"{sourceType}#{instant}"
                : $"{sourceType}#{instant}#{i:D4}";

            items.Add(new Dictionary<string, AttributeValue>(StringComparer.Ordinal)
            {
                [SyncArchiveTableInitializer.PartitionKeyAttribute] = Text(key),
                [RawJsonAttribute] = Text(rawResponseBodies[i]),
            });
        }

        return items;
    }

    /// <summary>Renders an instant in <see cref="InstantFormat"/>, normalizing its kind to UTC first.</summary>
    private static string FormatInstant(DateTime syncedAt)
    {
        var utc = syncedAt.Kind switch
        {
            DateTimeKind.Utc => syncedAt,
            DateTimeKind.Local => syncedAt.ToUniversalTime(),
            // Unspecified is treated as already-UTC rather than run through ToUniversalTime, which would
            // read it as local time and shift the key by the machine's offset.
            _ => DateTime.SpecifyKind(syncedAt, DateTimeKind.Utc),
        };

        return utc.ToString(InstantFormat, CultureInfo.InvariantCulture);
    }

    private static AttributeValue Text(string value)
    {
        return new AttributeValue { S = value };
    }

    private async Task WriteBatchAsync(List<WriteRequest> batch, CancellationToken cancellationToken)
    {
        var pending = batch;
        var backoff = InitialBackoff;

        for (var attempt = 1; ; attempt++)
        {
            var response = await _dynamoDb.BatchWriteItemAsync(
                new BatchWriteItemRequest
                {
                    RequestItems = new Dictionary<string, List<WriteRequest>>(StringComparer.Ordinal)
                    {
                        [_options.TableName] = pending,
                    },
                },
                cancellationToken);

            // A partial write is a success from BatchWriteItem's point of view: the leftovers come back
            // in UnprocessedItems with a 200, so not re-sending them loses data silently.
            if (response.UnprocessedItems is null
                || !response.UnprocessedItems.TryGetValue(_options.TableName, out var unprocessed)
                || unprocessed.Count == 0)
            {
                return;
            }

            if (attempt >= MaxBatchWriteAttempts)
            {
                throw new InvalidOperationException(
                    $"DynamoDB left {unprocessed.Count} sync archive item(s) unprocessed in table {_options.TableName} after {attempt} attempts.");
            }

            pending = unprocessed;
            await Task.Delay(backoff, cancellationToken);
            backoff *= 2;
        }
    }
}
