using Dollars2.Api.Models;
using Dollars2.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dollars2.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] AuthRequest request)
    {
        var result = await _authService.LoginAsync(request.Email);
        if (result.Error is not null)
        {
            return Unauthorized(result);
        }
        return Ok(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var result = await _authService.RefreshAsync(request.RefreshToken);
        if (result.Error is not null)
        {
            return Unauthorized(result);
        }
        return Ok(result);
    }
}
