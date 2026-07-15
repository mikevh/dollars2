using Serilog;
using Serilog.Events;

namespace Dollars2.Api.Logging;

public static class SerilogConfiguration
{
    /// <summary>
    /// Applies Dollars2's logging setup: console output plus a rolling file on disk. The file path
    /// is a Serilog path template (a date is substituted into the trailing <c>-</c> when it rolls),
    /// e.g. <c>logs/dollars2-.log</c> produces <c>logs/dollars2-20260714.log</c>. Kept as an
    /// extension so the file sink can be exercised in a test without booting the whole app.
    /// </summary>
    public static LoggerConfiguration ConfigureDollars2Logging(this LoggerConfiguration config, string logFilePath)
    {
        return config
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14);
    }
}
