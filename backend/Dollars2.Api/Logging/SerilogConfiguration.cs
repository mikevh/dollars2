using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Serilog.Sinks;
using Serilog;
using Serilog.Events;

namespace Dollars2.Api.Logging;

public static class SerilogConfiguration
{
    /// <summary>
    /// Applies Dollars2's logging setup: console output plus a rolling file on disk, and — when
    /// <paramref name="elasticsearchUri"/> is supplied — an Elasticsearch sink so logs are shipped
    /// to the Elasticsearch/Kibana stack running alongside the app.
    /// </summary>
    /// <param name="logFilePath">
    /// A Serilog path template (a date is substituted into the trailing <c>-</c> when it rolls),
    /// e.g. <c>logs/dollars2-.log</c> produces <c>logs/dollars2-20260714.log</c>.
    /// </param>
    /// <param name="elasticsearchUri">
    /// Base URI of the Elasticsearch node (e.g. <c>http://elasticsearch:9200</c>). When null or
    /// blank the Elasticsearch sink is omitted, leaving console + file logging unchanged — this is
    /// the case for local development, tests, and CI where no Elasticsearch instance is running.
    /// Kept as an extension so the sinks can be exercised in a test without booting the whole app.
    /// </param>
    public static LoggerConfiguration ConfigureDollars2Logging(
        this LoggerConfiguration config,
        string logFilePath,
        string? elasticsearchUri = null)
    {
        config
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14);

        if (!string.IsNullOrWhiteSpace(elasticsearchUri))
        {
            config.WriteTo.Elasticsearch(
                new[] { new Uri(elasticsearchUri) },
                options =>
                {
                    // Land logs in a single, discoverable data stream (logs-dollars2-default).
                    options.DataStream = new DataStreamName("logs", "dollars2");
                    // Create the backing index template on startup, but never let an unreachable or
                    // unhealthy Elasticsearch take the app down — logs still go to console and file.
                    options.BootstrapMethod = BootstrapMethod.Silent;
                });
        }

        return config;
    }
}
