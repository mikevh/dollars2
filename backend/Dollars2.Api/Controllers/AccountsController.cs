using Dollars2.Api.Models;
using Dollars2.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dollars2.Api.Controllers;

[Authorize]
[Route("api/accounts")]
public class AccountsController : DollarsControllerBase
{
    private readonly AccountService _accountService;
    private readonly AccountSyncArchiveService _syncArchiveService;

    public AccountsController(AccountService accountService, AccountSyncArchiveService syncArchiveService)
    {
        _accountService = accountService;
        _syncArchiveService = syncArchiveService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAccounts()
    {
        var userId = GetUserId();
        var groups = await _accountService.GetAccountGroupsAsync(userId);
        return Ok(DollarsApiResponse<IEnumerable<AccountGroupResponse>>.Success(groups));
    }

    /// <summary>
    /// One page of this account's sync archive, newest run first. Paged on the cursor the previous page
    /// returned as <c>nextBefore</c>, not on a page number: the archive only grows at the newest end, so
    /// an offset would shift under a client walking backwards through it.
    /// </summary>
    [HttpGet("{id}/sync-archive")]
    public async Task<IActionResult> GetSyncArchive(
        int id,
        [FromQuery] string? before,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var result = await _syncArchiveService.GetArchivePageAsync(id, GetUserId(), before, limit, cancellationToken);
        if (result.Error is not null)
        {
            // A dead archive is a dependency outage, not a bad request — the same carve-out the
            // transaction raw-history read makes.
            if (result.Error.Code == AccountSyncArchiveService.ArchiveUnavailableCode)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, result);
            }

            if (result.Error.Code == AccountSyncArchiveService.InvalidCursorCode)
            {
                return BadRequest(result);
            }

            return NotFound(result);
        }

        return Ok(result);
    }
}
