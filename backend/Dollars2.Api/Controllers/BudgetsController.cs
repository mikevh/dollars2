using Dollars2.Api.Models;
using Dollars2.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dollars2.Api.Controllers;

[Authorize]
[Route("api/budgets")]
public class BudgetsController : DollarsControllerBase
{
    private readonly BudgetService _budgetService;

    public BudgetsController(BudgetService budgetService)
    {
        _budgetService = budgetService;
    }

    [HttpGet("{year}/{month}")]
    public async Task<IActionResult> GetBudget(int year, int month)
    {
        var result = await _budgetService.GetBudgetAsync(GetUserId(), year, month);
        if (result.Error is not null)
        {
            return NotFound(result);
        }
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateBudget([FromBody] CreateBudgetRequest request)
    {
        var result = await _budgetService.CreateBudgetAsync(GetUserId(), request.Year, request.Month);
        if (result.Error is not null)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpPost("{budgetId}/groups")]
    public async Task<IActionResult> CreateGroup(int budgetId, [FromBody] CreateGroupRequest request)
    {
        var result = await _budgetService.CreateGroupAsync(budgetId, request.Name, GetUserId());
        if (result.Error is not null)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }
}
