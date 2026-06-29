using Dollars2.Api.Repositories;

namespace Dollars2.Api.Services;

public class BankSyncHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BankSyncHostedService> _logger;
    // Runs frequently; per-provider MinSyncInterval throttles how often each
    // provider is actually synced for a given user.
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

    public BankSyncHostedService(IServiceScopeFactory scopeFactory, ILogger<BankSyncHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunSyncAsync(stoppingToken);
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
}
