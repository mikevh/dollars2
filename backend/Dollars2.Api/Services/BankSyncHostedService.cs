using Dollars2.Api.Repositories;

namespace Dollars2.Api.Services;

public class BankSyncHostedService : BackgroundService
{
    private const int DefaultSyncLogRetentionDays = 90;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BankSyncHostedService> _logger;
    // Runs frequently; per-provider MinSyncInterval throttles how often each
    // provider is actually synced for a given user.
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);
    private readonly int _syncLogRetentionDays;

    public BankSyncHostedService(IServiceScopeFactory scopeFactory, ILogger<BankSyncHostedService> logger, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _syncLogRetentionDays = config.GetValue<int?>("Retention:SyncLogDays") ?? DefaultSyncLogRetentionDays;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunSyncAsync(stoppingToken);
            await RunPruneAsync(stoppingToken);
            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task RunSyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var userRepo = scope.ServiceProvider.GetRequiredService<UserRepository>();
            var syncService = scope.ServiceProvider.GetRequiredService<BankSyncService>();

            var userIds = await userRepo.GetAllIdsAsync();
            await syncService.SyncAllUsersAsync(userIds, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // App is shutting down — not an error.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled bank sync failed");
        }
    }

    // SyncLog is append-only and there is no SQL Agent in the self-hosted container, so the app
    // drives its own retention off the same loop. Deliberately outside RunSyncAsync's try/catch:
    // a failed prune must never stop syncing, and a failed sync must not skip the prune.
    private async Task RunPruneAsync(CancellationToken cancellationToken)
    {
        // A sync that was cut short by shutdown falls through to here; don't start a DELETE the
        // host is only going to wait on. Retention is not time-critical — the next run gets it.
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var syncLogRepo = scope.ServiceProvider.GetRequiredService<SyncLogRepository>();

            var deleted = await syncLogRepo.PruneAsync(_syncLogRetentionDays);

            if (deleted > 0)
            {
                _logger.LogInformation("Pruned {Count} sync log rows older than {RetentionDays} days", deleted, _syncLogRetentionDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync log prune failed");
        }
    }
}
