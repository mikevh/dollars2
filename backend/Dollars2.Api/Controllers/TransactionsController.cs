using Dollars2.Api.Models;
using Dollars2.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dollars2.Api.Controllers;

[Authorize]
[Route("api/transactions")]
public class TransactionsController : DollarsControllerBase
{
    private readonly TransactionService _transactionService;

    public TransactionsController(TransactionService transactionService)
    {
        _transactionService = transactionService;
    }

    [HttpGet("counts")]
    public async Task<IActionResult> GetCounts()
    {
        var result = await _transactionService.GetCountsAsync(GetUserId());
        return Ok(result);
    }

    [HttpGet("new")]
    public async Task<IActionResult> GetNew()
    {
        var result = await _transactionService.GetNewAsync(GetUserId());
        return Ok(result);
    }

    [HttpGet("tracked")]
    public async Task<IActionResult> GetTracked([FromQuery] DateOnly fromDate)
    {
        var result = await _transactionService.GetTrackedAsync(GetUserId(), fromDate);
        return Ok(result);
    }

    [HttpGet("deleted")]
    public async Task<IActionResult> GetDeleted()
    {
        var result = await _transactionService.GetDeletedAsync(GetUserId());
        return Ok(result);
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
    {
        var result = await _transactionService.GetPendingAsync(GetUserId());
        return Ok(result);
    }

    [HttpGet("by-account/{accountId}")]
    public async Task<IActionResult> GetByAccount(
        int accountId,
        [FromQuery] int page = 1,
        [FromQuery] int size = 100,
        [FromQuery] string sort = "date",
        [FromQuery] string dir = "desc",
        [FromQuery] string? q = null)
    {
        if (page < 1)
        {
            page = 1;
        }
        if (size < 1)
        {
            size = 100;
        }
        if (size > 500)
        {
            size = 500;
        }
        sort = sort?.ToLowerInvariant() switch
        {
            "description" => "description",
            "amount" => "amount",
            _ => "date",
        };
        dir = dir?.ToLowerInvariant() == "asc" ? "asc" : "desc";

        var result = await _transactionService.GetByAccountAsync(accountId, GetUserId(), page, size, sort, dir, q);
        if (result.Error is not null)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTransactionRequest request)
    {
        var result = await _transactionService.CreateAsync(GetUserId(), request.Date, request.Description, request.Amount, request.Notes, request.Payee, request.Memo);
        return Ok(result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTransactionRequest request)
    {
        var result = await _transactionService.UpdateAsync(id, GetUserId(), request.Date, request.Description, request.Amount, request.Notes);
        if (result.Error is not null)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> SoftDelete(int id)
    {
        var result = await _transactionService.SoftDeleteAsync(id, GetUserId());
        if (result.Error is not null)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(int id)
    {
        var result = await _transactionService.RestoreAsync(id, GetUserId());
        if (result.Error is not null)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpDelete("{id}/permanent")]
    public async Task<IActionResult> HardDelete(int id)
    {
        var result = await _transactionService.HardDeleteAsync(id, GetUserId());
        if (result.Error is not null)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpPost("{id}/assign")]
    public async Task<IActionResult> Assign(int id, [FromBody] AssignTransactionRequest request)
    {
        var result = await _transactionService.AssignAsync(id, request.LineItemId, GetUserId());
        if (result.Error is not null)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpPost("{id}/unassign")]
    public async Task<IActionResult> Unassign(int id)
    {
        var result = await _transactionService.UnassignAsync(id, GetUserId());
        if (result.Error is not null)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpPut("{id}/assignments")]
    public async Task<IActionResult> SetAssignments(int id, [FromBody] SetAssignmentsRequest request)
    {
        var assignments = request.Assignments
            .Select(a => (a.LineItemId, a.Amount))
            .ToList();
        var result = await _transactionService.SetAssignmentsAsync(id, assignments, GetUserId());
        if (result.Error is not null)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }
}
