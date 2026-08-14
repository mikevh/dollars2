using System.Security.Cryptography;
using Dollars2.Api.Models;
using Dollars2.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace Dollars2.Api.Controllers;

[Route("api/auth")]
public class AuthController : DollarsControllerBase
{
    private const string CeremonyCookieName = "dollars2_passkey_ceremony";
    private const string CeremonyProtectorPurpose = "Dollars2.PasskeyCeremony";

    private readonly AuthService _authService;
    private readonly IDataProtector _ceremonyProtector;

    public AuthController(AuthService authService, IDataProtectionProvider dataProtectionProvider)
    {
        _authService = authService;
        _ceremonyProtector = dataProtectionProvider.CreateProtector(CeremonyProtectorPurpose);
    }

    [HttpPost("passkey/register/options")]
    [AllowAnonymous]
    public async Task<IActionResult> PasskeyRegisterOptions([FromBody] PasskeyRegistrationOptionsRequest request)
    {
        var (result, attestationState) = await _authService.GetPasskeyRegistrationOptionsAsync(request.Email, request.RegistrationKey, HttpContext);
        if (result.Error is not null)
        {
            return Unauthorized(result);
        }
        SetCeremonyCookie(attestationState!);
        return Ok(result);
    }

    [HttpPost("passkey/register/complete")]
    [AllowAnonymous]
    public async Task<IActionResult> PasskeyRegisterComplete([FromBody] PasskeyRegistrationCompleteRequest request)
    {
        var attestationState = ReadCeremonyCookie();
        var result = await _authService.CompletePasskeyRegistrationAsync(request.CredentialJson, attestationState, HttpContext);
        ClearCeremonyCookie();
        if (result.Error is not null)
        {
            return Unauthorized(result);
        }
        return Ok(result);
    }

    [HttpPost("passkey/login/options")]
    [AllowAnonymous]
    public async Task<IActionResult> PasskeyLoginOptions([FromBody] PasskeyLoginOptionsRequest request)
    {
        var (result, assertionState) = await _authService.GetPasskeyLoginOptionsAsync(request.Email, HttpContext);
        if (result.Error is not null)
        {
            return Unauthorized(result);
        }
        SetCeremonyCookie(assertionState!);
        return Ok(result);
    }

    [HttpPost("passkey/login/complete")]
    [AllowAnonymous]
    public async Task<IActionResult> PasskeyLoginComplete([FromBody] PasskeyLoginCompleteRequest request)
    {
        var assertionState = ReadCeremonyCookie();
        var result = await _authService.CompletePasskeyLoginAsync(request.CredentialJson, assertionState, HttpContext);
        ClearCeremonyCookie();
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

    private void SetCeremonyCookie(string state)
    {
        Response.Cookies.Append(CeremonyCookieName, _ceremonyProtector.Protect(state), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddMinutes(5),
        });
    }

    private string? ReadCeremonyCookie()
    {
        if (!Request.Cookies.TryGetValue(CeremonyCookieName, out var protectedState))
        {
            return null;
        }

        try
        {
            return _ceremonyProtector.Unprotect(protectedState);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private void ClearCeremonyCookie() => Response.Cookies.Delete(CeremonyCookieName);
}
