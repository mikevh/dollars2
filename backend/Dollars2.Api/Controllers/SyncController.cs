using Dollars2.Api.Models;
using Dollars2.Api.Repositories;
using Dollars2.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dollars2.Api.Controllers;

[Authorize]
[Route("api/sync")]
public class SyncController : DollarsControllerBase
{
    private readonly BankSyncService _syncService;
    private readonly SyncLockService _syncLock;
    private readonly AccountRepository _accountRepo;
    private readonly SyncLogRepository _syncLogRepo;

    public SyncController(BankSyncService syncService, SyncLockService syncLock, AccountRepository accountRepo, SyncLogRepository syncLogRepo)
    {
        _syncService = syncService;
        _syncLock = syncLock;
        _accountRepo = accountRepo;
        _syncLogRepo = syncLogRepo;
    }

    [HttpPost]
    public async Task<IActionResult> Sync()
    {
        var userId = GetUserId();
        if (!_syncLock.TryAcquire(userId))
        {
            return Conflict(DollarsApiResponse<IEnumerable<SyncResult>>.Fail("A sync is already in progress.", "SYNC_IN_PROGRESS"));
        }
        try
        {
            var results = await _syncService.SyncForUserAsync(userId, cancellationToken: HttpContext.RequestAborted);
            return Ok(DollarsApiResponse<IEnumerable<SyncResult>>.Success(results));
        }
        finally
        {
            _syncLock.Release(userId);
        }
    }

    [HttpPost("connection/{connectionId}")]
    public async Task<IActionResult> SyncConnection(string connectionId)
    {
        var userId = GetUserId();
        if (!_syncLock.TryAcquire(userId))
        {
            return Conflict(DollarsApiResponse<IEnumerable<SyncResult>>.Fail("A sync is already in progress.", "SYNC_IN_PROGRESS"));
        }
        try
        {
            var results = await _syncService.SyncConnectionForUserAsync(userId, connectionId, HttpContext.RequestAborted);
            if (results is null)
            {
                return NotFound(DollarsApiResponse<IEnumerable<SyncResult>>.Fail("Connection not found.", "CONNECTION_NOT_FOUND"));
            }
            return Ok(DollarsApiResponse<IEnumerable<SyncResult>>.Success(results));
        }
        finally
        {
            _syncLock.Release(userId);
        }
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var userId = GetUserId();
        var accounts = (await _accountRepo.GetByUserIdAsync(userId)).ToList();
        var syncable = accounts.Where(a => a.SourceType != SyncConstants.SourceTypeManual).ToList();

        if (syncable.Count == 0)
        {
            return Ok(DollarsApiResponse<IEnumerable<SyncStatusResponse>>.Success(Enumerable.Empty<SyncStatusResponse>()));
        }

        var logs = (await _syncLogRepo.GetLatestPerAccountAsync(syncable.Select(a => a.Id)))
            .ToDictionary(l => l.AccountId);

        var statuses = syncable.Select(a =>
        {
            logs.TryGetValue(a.Id, out var log);
            return new SyncStatusResponse
            {
                AccountId = a.Id,
                AccountName = a.Name,
                LastSyncedAt = log?.SyncedAt,
                LastStatus = log?.Status,
                LastTransactionCount = log?.TransactionCount,
                LastErrorMessage = log?.ErrorMessage,
            };
        });

        return Ok(DollarsApiResponse<IEnumerable<SyncStatusResponse>>.Success(statuses));
    }
}
