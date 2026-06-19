using Dollars2.Api.Models;
using Dollars2.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dollars2.Api.Controllers;

[Authorize]
[Route("api/groups")]
public class GroupsController : DollarsControllerBase
{
    private readonly BudgetService _budgetService;

    public GroupsController(BudgetService budgetService)
    {
        _budgetService = budgetService;
    }

    [HttpPut("{groupId}")]
    public async Task<IActionResult> UpdateGroup(int groupId, [FromBody] UpdateGroupRequest request)
    {
        var result = await _budgetService.UpdateGroupAsync(groupId, request.Name, GetUserId());
        if (result.Error is not null)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpDelete("{groupId}")]
    public async Task<IActionResult> DeleteGroup(int groupId)
    {
        var result = await _budgetService.DeleteGroupAsync(groupId, GetUserId());
        if (result.Error is not null)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpPut("reorder")]
    public async Task<IActionResult> ReorderGroups([FromQuery] int budgetId, [FromBody] ReorderRequest request)
    {
        var result = await _budgetService.ReorderGroupsAsync(budgetId, request.Ids, GetUserId());
        if (result.Error is not null)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpPost("{groupId}/line-items")]
    public async Task<IActionResult> CreateLineItem(int groupId, [FromBody] CreateLineItemRequest request)
    {
        var result = await _budgetService.CreateLineItemAsync(groupId, request.Name, request.PlannedAmount, GetUserId());
        if (result.Error is not null)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpPut("{groupId}/line-items/reorder")]
    public async Task<IActionResult> ReorderLineItems(int groupId, [FromBody] ReorderRequest request)
    {
        var result = await _budgetService.ReorderLineItemsAsync(groupId, request.Ids, GetUserId());
        if (result.Error is not null)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }
}
