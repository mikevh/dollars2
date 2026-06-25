using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Dollars2.Api.Controllers;

[ApiController]
public abstract class DollarsControllerBase : ControllerBase
{
    protected int GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim is null)
        {
            throw new UnauthorizedAccessException("Missing user identity claim.");
        }
        return int.Parse(claim.Value);
    }
}
