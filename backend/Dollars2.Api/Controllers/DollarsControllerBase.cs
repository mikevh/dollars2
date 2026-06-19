using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Dollars2.Api.Controllers;

[ApiController]
public abstract class DollarsControllerBase : ControllerBase
{
    protected int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
}
