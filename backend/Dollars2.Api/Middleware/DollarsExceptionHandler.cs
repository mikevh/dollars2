using Dollars2.Api.Json;
using Dollars2.Api.Models;
using Microsoft.AspNetCore.Diagnostics;

namespace Dollars2.Api.Middleware;

public class DollarsExceptionHandler : IExceptionHandler
{
    private readonly ILogger<DollarsExceptionHandler> _logger;

    public DollarsExceptionHandler(ILogger<DollarsExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        _logger.LogError(ex, "Unhandled exception: {message}", ex.Message);

        if (ex is UnauthorizedAccessException)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(
                DollarsApiResponse<object>.Fail("Unauthorized.", "UNAUTHORIZED"),
                Dollars2JsonOptions.CreateWebOptions(),
                ct);
            return true;
        }

        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await ctx.Response.WriteAsJsonAsync(
            DollarsApiResponse<object>.Fail("An unexpected error occurred.", "INTERNAL_ERROR"),
            Dollars2JsonOptions.CreateWebOptions(),
            ct);
        return true;
    }
}
