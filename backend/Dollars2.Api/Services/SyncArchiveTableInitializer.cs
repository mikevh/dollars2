using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Dollars2.Api.Data;

namespace Dollars2.Api.Services;

/// <summary>
/// Creates the sync archive's DynamoDB table on startup if it is not already there.
/// </summary>
/// <remarks>
/// DynamoDB has no schema beyond its key attributes, so there is nothing here resembling the numbered
/// SQL migration chain — an idempotent create-if-absent at startup covers development, the home-server
/// deploy, and the test harness with one code path. Running as an <see cref="IHostedService"/> rather
/// than lazily on first write means table readiness is settled before the first sync.
/// </remarks>
public class SyncArchiveTableInitializer : IHostedService
{
    /// <summary>Partition key: <c>USER#{userId}#ACCT#{accountId}</c>.</summary>
    public const string PartitionKeyAttribute = "pk";

    /// <summary>
    /// Sort key: <c>TXN#{providerTransactionId}#{syncedAt}</c>, <c>REMOVED#{providerTransactionId}#{syncedAt}</c>,
    /// <c>ACCTMETA#{syncedAt}</c>, or <c>ERROR#{syncedAt}#{seq}</c>. The trailing instant is ISO-8601 UTC
    /// with a <c>Z</c> marker, which sorts lexicographically in chronological order — that is what makes
    /// the composite sort keys usable.
    /// </summary>
    public const string SortKeyAttribute = "sk";

    /// <summary>Bare ISO-8601 UTC instant, indexed by <see cref="SyncedAtIndexName"/>.</summary>
    public const string SyncedAtAttribute = "syncedAt";

    /// <summary>Index behind "show me this account's archive, newest first".</summary>
    public const string SyncedAtIndexName = "LSI_SyncedAt";

    private static readonly TimeSpan ActiveTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ActivePollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly DynamoDbOptions _options;
    private readonly ILogger<SyncArchiveTableInitializer> _logger;

    public SyncArchiveTableInitializer(
        IAmazonDynamoDB dynamoDb,
        DynamoDbOptions options,
        ILogger<SyncArchiveTableInitializer> logger)
    {
        _dynamoDb = dynamoDb;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await DescribeAsync(_options.TableName, cancellationToken) is not null)
            {
                _logger.LogDebug("Sync archive table {TableName} already exists", _options.TableName);
                return;
            }

            try
            {
                await _dynamoDb.CreateTableAsync(BuildCreateTableRequest(_options.TableName), cancellationToken);
            }
            catch (ResourceInUseException)
            {
                // Another process won the race between DescribeTable and CreateTable. Fall through to
                // the wait below — the table it created is the table this one would have created.
                _logger.LogDebug("Sync archive table {TableName} was created concurrently", _options.TableName);
            }

            await WaitForActiveAsync(cancellationToken);

            _logger.LogInformation(
                "Created sync archive table {TableName} with local secondary index {IndexName}",
                _options.TableName,
                SyncedAtIndexName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host is shutting down before the table settled — not an error.
        }
        catch (Exception ex)
        {
            // Sync archiving is best-effort, so an unreachable or unhappy DynamoDB must never keep the
            // app from serving budgets. The next startup tries again.
            _logger.LogError(
                ex,
                "Could not ensure sync archive table {TableName} exists at {ServiceUrl}; sync archiving will not work until this is resolved",
                _options.TableName,
                _options.ServiceUrl);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// The archive table's full schema. Exposed so it can be asserted directly — the billing mode and
    /// the absence of provisioned throughput are not observable through <c>DescribeTable</c> against
    /// dynamodb-local.
    /// </summary>
    public static CreateTableRequest BuildCreateTableRequest(string tableName)
    {
        return new CreateTableRequest
        {
            TableName = tableName,
            KeySchema =
            [
                new KeySchemaElement { AttributeName = PartitionKeyAttribute, KeyType = KeyType.HASH },
                new KeySchemaElement { AttributeName = SortKeyAttribute, KeyType = KeyType.RANGE },
            ],
            AttributeDefinitions =
            [
                new AttributeDefinition { AttributeName = PartitionKeyAttribute, AttributeType = ScalarAttributeType.S },
                new AttributeDefinition { AttributeName = SortKeyAttribute, AttributeType = ScalarAttributeType.S },
                new AttributeDefinition { AttributeName = SyncedAtAttribute, AttributeType = ScalarAttributeType.S },
            ],
            // An LSI (rather than a GSI) because the partition is already scoped to a single account.
            // It must be declared at table-creation time and can never be added afterwards, so it ships
            // with the first table or not at all. The cost is that it caps a single partition — here,
            // one account — at 10GB; at this app's volume that is decades away.
            LocalSecondaryIndexes =
            [
                new LocalSecondaryIndex
                {
                    IndexName = SyncedAtIndexName,
                    KeySchema =
                    [
                        new KeySchemaElement { AttributeName = PartitionKeyAttribute, KeyType = KeyType.HASH },
                        new KeySchemaElement { AttributeName = SyncedAtAttribute, KeyType = KeyType.RANGE },
                    ],
                    Projection = new Projection { ProjectionType = ProjectionType.ALL },
                },
            ],
            // dynamodb-local ignores throughput settings entirely, so this has no runtime effect. It is
            // chosen over provisioned throughput only because there is no capacity here to plan for —
            // there is no AWS account behind this table and this schema is not a migration path to one.
            BillingMode = BillingMode.PAY_PER_REQUEST,
        };
    }

    private async Task<TableDescription?> DescribeAsync(string tableName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _dynamoDb.DescribeTableAsync(tableName, cancellationToken);
            return response.Table;
        }
        catch (ResourceNotFoundException)
        {
            return null;
        }
    }

    private async Task WaitForActiveAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + ActiveTimeout;

        while (true)
        {
            var table = await DescribeAsync(_options.TableName, cancellationToken);

            if (table is not null && table.TableStatus == TableStatus.ACTIVE)
            {
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Sync archive table {_options.TableName} did not become ACTIVE within {ActiveTimeout.TotalSeconds:0} seconds.");
            }

            await Task.Delay(ActivePollInterval, cancellationToken);
        }
    }
}
