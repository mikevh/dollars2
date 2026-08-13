using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Dollars2.Api.Data;
using Dollars2.Api.Models;
using Dollars2.Api.Providers;
using Dollars2.Api.Services;

namespace Dollars2.Api.Repositories;

/// <summary>
/// Writes the raw payloads from one account's sync into the DynamoDB archive table.
/// </summary>
/// <remarks>
/// Append-only and versioned by <c>syncedAt</c>: re-seeing a transaction never overwrites the previous
/// sighting, it adds another one. That is the whole point — pending→posted transitions, description
/// rewrites and amount corrections are only visible as a sequence of versions. The cost, accepted
/// knowingly, is that the deliberate 7-day sync overlap and the 180-day full resync re-archive most
/// transactions many times with identical payloads.
///
/// Unlike every other repository here this one is not Dapper over <see cref="DbSession"/> and takes no
/// part in its transaction: DynamoDB cannot join an MSSQL transaction, and enrolling a best-effort
/// external write in it would defeat the point of it being best-effort. Callers are expected to treat
/// failures here as non-fatal.
/// </remarks>
public class SyncArchiveRepository
{
    /// <summary>GUID shared by every item written by one sync run, so a run can be reassembled.</summary>
    public const string SyncRunIdAttribute = "syncRunId";

    public const string UserIdAttribute = "userId";
    public const string AccountIdAttribute = "accountId";
    public const string SourceTypeAttribute = "sourceType";
    public const string ItemTypeAttribute = "itemType";
    public const string ProviderTransactionIdAttribute = "providerTransactionId";

    /// <summary>The provider's payload, verbatim. Absent on <see cref="ItemTypeRemoved"/> items.</summary>
    public const string RawJsonAttribute = "rawJson";

    public const string ItemTypeTransaction = "Transaction";
    public const string ItemTypeRemoved = "Removed";
    public const string ItemTypeAccountMetadata = "AccountMetadata";
    public const string ItemTypeProviderError = "ProviderError";
    public const string ItemTypeSkippedTransaction = "SkippedTransaction";

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
    /// ISO-8601 UTC with an explicit Z, to millisecond precision. This string is both the sort-key
    /// suffix and the LSI range key, so it has to be fixed-width and lexicographically chronological.
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
    /// DynamoDB that accepts connections and then goes silent would hold up each account's sync for the
    /// SDK's full retry budget — minutes, per account, for a write nobody is waiting on. Settable so
    /// tests can assert the bound without sitting through it; DI leaves it at the default.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Archives everything <paramref name="result"/> carries for <paramref name="account"/>. Callers pass
    /// one <paramref name="syncRunId"/> and one <paramref name="syncedAt"/> per sync run so that every
    /// account in a connection group lands under the same run.
    /// </summary>
    /// <remarks>
    /// Throws on failure rather than swallowing: "best-effort" is the caller's policy to apply, and a
    /// repository that silently reported success would make an outage invisible.
    /// </remarks>
    public async Task ArchiveAsync(
        Account account,
        ProviderSyncResult result,
        Guid syncRunId,
        DateTime syncedAt,
        CancellationToken cancellationToken = default)
    {
        var items = BuildItems(account, result, syncRunId, syncedAt);
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

    /// <summary>The partition every item for one account lives in.</summary>
    public static string PartitionKeyFor(int userId, int accountId)
    {
        return $"USER#{userId}#ACCT#{accountId}";
    }

    /// <summary>
    /// The items one sync result becomes. Pure, and public so the key construction can be asserted
    /// directly — the sort keys are the schema here, and they are far easier to get wrong than to check.
    /// </summary>
    public static IReadOnlyList<Dictionary<string, AttributeValue>> BuildItems(
        Account account,
        ProviderSyncResult result,
        Guid syncRunId,
        DateTime syncedAt)
    {
        var instant = FormatInstant(syncedAt);
        var partitionKey = PartitionKeyFor(account.UserId, account.Id);

        // Two items with the same key in one BatchWriteItem request are rejected as a batch, not
        // individually, so a provider echoing the same transaction twice in one payload would cost the
        // account its entire archive for that run. Last sighting wins — same key means the payload is
        // the only thing that can differ.
        var byKey = new Dictionary<string, Dictionary<string, AttributeValue>>(StringComparer.Ordinal);
        var keyOrder = new List<string>();

        void Add(string sortKey, string itemType, string? rawJson, string? providerTransactionId)
        {
            var item = new Dictionary<string, AttributeValue>(StringComparer.Ordinal)
            {
                [SyncArchiveTableInitializer.PartitionKeyAttribute] = Text(partitionKey),
                [SyncArchiveTableInitializer.SortKeyAttribute] = Text(sortKey),
                [SyncArchiveTableInitializer.SyncedAtAttribute] = Text(instant),
                [SyncRunIdAttribute] = Text(syncRunId.ToString()),
                [UserIdAttribute] = Number(account.UserId),
                [AccountIdAttribute] = Number(account.Id),
                [SourceTypeAttribute] = Text(account.SourceType),
                [ItemTypeAttribute] = Text(itemType),
            };

            if (providerTransactionId is not null)
            {
                item[ProviderTransactionIdAttribute] = Text(providerTransactionId);
            }

            if (rawJson is not null)
            {
                item[RawJsonAttribute] = Text(rawJson);
            }

            if (!byKey.ContainsKey(sortKey))
            {
                keyOrder.Add(sortKey);
            }

            byKey[sortKey] = item;
        }

        // The provider transaction id sits in the middle of the sort key and may itself contain '#', so
        // these are only unambiguous read right-to-left: the trailing instant is fixed-width.
        foreach (var transaction in result.Upserts)
        {
            Add(
                $"TXN#{transaction.ProviderTransactionId}#{instant}",
                ItemTypeTransaction,
                transaction.RawJson,
                transaction.ProviderTransactionId);
        }

        // No rawJson: a removal's entire payload by the time it reaches here is the id, which is already
        // its own attribute. Copying it into rawJson would be a synthesized payload posing as a verbatim
        // one, which is exactly what this table is supposed to be trustworthy about.
        foreach (var removedId in result.RemovedProviderTransactionIds)
        {
            Add($"REMOVED#{removedId}#{instant}", ItemTypeRemoved, null, removedId);
        }

        if (result.AccountMetadataJson is not null)
        {
            Add($"ACCTMETA#{instant}", ItemTypeAccountMetadata, result.AccountMetadataJson, null);
        }

        // The sequence numbers are zero-padded for the same reason the instants are ISO-8601: these keys
        // sort lexicographically, and an unpadded 10 would sort ahead of 2.
        for (var i = 0; i < result.ErrorsJson.Count; i++)
        {
            Add($"ERROR#{instant}#{i:D4}", ItemTypeProviderError, result.ErrorsJson[i], null);
        }

        // Transactions the parser rejected. They have no provider transaction id to key on — that they
        // could not be mapped is why they are here — so they are sequenced like errors.
        for (var i = 0; i < result.SkippedTransactionsJson.Count; i++)
        {
            Add($"SKIPPED#{instant}#{i:D4}", ItemTypeSkippedTransaction, result.SkippedTransactionsJson[i], null);
        }

        return keyOrder.Select(key => byKey[key]).ToList();
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

    private static AttributeValue Number(int value)
    {
        return new AttributeValue { N = value.ToString(CultureInfo.InvariantCulture) };
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
