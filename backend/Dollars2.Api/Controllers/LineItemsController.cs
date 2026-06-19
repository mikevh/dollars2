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

    public LineItemsController(BudgetService budgetService)
    {
        _budgetService = budgetService;
    }

    [HttpPut("{lineItemId}")]
    public async Task<IActionResult> UpdateLineItem(int lineItemId, [FromBody] UpdateLineItemRequest request)
    {
        var result = await _budgetService.UpdateLineItemAsync(lineItemId, request.Name, request.PlannedAmount, GetUserId());
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
