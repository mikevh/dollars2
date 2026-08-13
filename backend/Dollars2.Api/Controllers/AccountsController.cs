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

    public AccountsController(AccountService accountService)
    {
        _accountService = accountService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAccounts()
    {
        var userId = GetUserId();
        var groups = await _accountService.GetAccountGroupsAsync(userId);
        return Ok(DollarsApiResponse<IEnumerable<AccountGroupResponse>>.Success(groups));
    }
}
