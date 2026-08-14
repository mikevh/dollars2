using System.Security.Cryptography;
using System.Text.Json;
using Dollars2.Api.Models;
using Dollars2.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace Dollars2.Api.Controllers;

[Route("api/auth")]
public class AuthController : DollarsControllerBase
{
    private const string RegistrationCookieName = "dollars2_passkey_register_ceremony";
    private const string LoginCookieName = "dollars2_passkey_login_ceremony";
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
        // Binds the registration key that was validated at this step into the cookie, so
        // completion can confirm it hasn't been rotated mid-ceremony rather than just checking
        // a key is still set at all.
        SetCeremonyCookie(RegistrationCookieName, new RegistrationCeremonyState(request.RegistrationKey, attestationState!));
        return Ok(result);
    }

    [HttpPost("passkey/register/complete")]
    [AllowAnonymous]
    public async Task<IActionResult> PasskeyRegisterComplete([FromBody] PasskeyRegistrationCompleteRequest request)
    {
        var state = ReadCeremonyCookie<RegistrationCeremonyState>(RegistrationCookieName);
        var result = await _authService.CompletePasskeyRegistrationAsync(request.CredentialJson, state?.RegistrationKey, state?.AttestationState, HttpContext);
        ClearCeremonyCookie(RegistrationCookieName);
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
        SetCeremonyCookie(LoginCookieName, new LoginCeremonyState(assertionState!));
        return Ok(result);
    }

    [HttpPost("passkey/login/complete")]
    [AllowAnonymous]
    public async Task<IActionResult> PasskeyLoginComplete([FromBody] PasskeyLoginCompleteRequest request)
    {
        var state = ReadCeremonyCookie<LoginCeremonyState>(LoginCookieName);
        var result = await _authService.CompletePasskeyLoginAsync(request.CredentialJson, state?.AssertionState, HttpContext);
        ClearCeremonyCookie(LoginCookieName);
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

    private void SetCeremonyCookie<T>(string cookieName, T state)
    {
        var protectedState = _ceremonyProtector.Protect(JsonSerializer.Serialize(state));
        Response.Cookies.Append(cookieName, protectedState, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddMinutes(5),
        });
    }

    private T? ReadCeremonyCookie<T>(string cookieName) where T : class
    {
        if (!Request.Cookies.TryGetValue(cookieName, out var protectedState))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(_ceremonyProtector.Unprotect(protectedState));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return null;
        }
    }

    private void ClearCeremonyCookie(string cookieName) => Response.Cookies.Delete(cookieName);

    private sealed record RegistrationCeremonyState(string RegistrationKey, string AttestationState);

    private sealed record LoginCeremonyState(string AssertionState);
}
