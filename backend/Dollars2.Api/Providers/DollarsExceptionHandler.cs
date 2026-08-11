using Microsoft.AspNetCore.Diagnostics;

namespace Dollars2.Api.Providers;

public class DollarsExceptionHandler : IExceptionHandler
{
    private readonly ILogger _logger;

    public DollarsExceptionHandler(ILogger<DollarsExceptionHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        _logger.LogError(ex, "Unhandled exception: {message}", ex.Message);
        return ValueTask.FromResult(true);
    }
}
