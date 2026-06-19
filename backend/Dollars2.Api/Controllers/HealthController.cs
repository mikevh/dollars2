using Dollars2.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dollars2.Api.Controllers;

[Route("api/[controller]")]
public class HealthController : DollarsControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Get()
    {
        return Ok(DollarsApiResponse<object>.Success(new { status = "healthy", timestamp = DateTime.UtcNow }));
    }
}
