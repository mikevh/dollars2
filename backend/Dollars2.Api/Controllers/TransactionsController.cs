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

    [HttpGet("new")]
    public async Task<IActionResult> GetNew()
    {
        var result = await _transactionService.GetNewAsync(GetUserId());
        return Ok(result);
    }

    [HttpGet("tracked")]
    public async Task<IActionResult> GetTracked([FromQuery] DateTime fromDate)
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

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTransactionRequest request)
    {
        var result = await _transactionService.CreateAsync(GetUserId(), request.Date, request.Description, request.Amount, request.Notes);
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
}
