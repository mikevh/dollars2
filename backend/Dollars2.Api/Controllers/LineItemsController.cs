using Dollars2.Api.Models;
using Dollars2.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dollars2.Api.Controllers;

[Authorize]
[Route("api/line-items")]
public class LineItemsController : DollarsControllerBase
{
    private readonly BudgetService _budgetService;
    private readonly TransactionService _transactionService;

    public LineItemsController(BudgetService budgetService, TransactionService transactionService)
    {
        _budgetService = budgetService;
        _transactionService = transactionService;
    }

    [HttpGet("{lineItemId}/activity")]
    public async Task<IActionResult> GetActivity(int lineItemId)
    {
        var result = await _transactionService.GetByLineItemAsync(lineItemId, GetUserId());
        if (result.Error is not null)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpPut("{lineItemId}")]
    public async Task<IActionResult> UpdateLineItem(int lineItemId, [FromBody] UpdateLineItemRequest request)
    {
        var result = await _budgetService.UpdateLineItemAsync(lineItemId, request.Name, request.PlannedAmount, request.Notes, GetUserId());
        if (result.Error is not null)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpDelete("{lineItemId}")]
    public async Task<IActionResult> DeleteLineItem(int lineItemId)
    {
        var result = await _budgetService.DeleteLineItemAsync(lineItemId, GetUserId());
        if (result.Error is not null)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }
}
