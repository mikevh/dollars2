using Dollars2.Api.Logging;
using Serilog;

namespace Dollars2.Tests;

// Verifies the acceptance check for the file-logging sprint: a logger built with the app's
// configuration actually writes log entries to a file on disk.
public class SerilogConfigurationTests
{
    [Fact]
    public void Configured_logger_writes_entries_to_a_file_on_disk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "d2log-" + Guid.NewGuid().ToString("N"));
        var pathTemplate = Path.Combine(dir, "dollars2-.log");

        using (var logger = new LoggerConfiguration().ConfigureDollars2Logging(pathTemplate).CreateLogger())
        {
            logger.Information("sprint-file-logging marker {Value}", 42);
        }

        try
        {
            var written = Directory.GetFiles(dir);
            Assert.Single(written);
            var content = File.ReadAllText(written[0]);
            Assert.Contains("sprint-file-logging marker 42", content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // With an Elasticsearch URI supplied the sink is added, but building the logger and writing to
    // it must not throw even when Elasticsearch is unreachable — the app must keep running (and keep
    // logging to console + file) regardless of the log store's health.
    [Fact]
    public void Configured_logger_with_elasticsearch_uri_still_writes_to_file_without_throwing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "d2log-" + Guid.NewGuid().ToString("N"));
        var pathTemplate = Path.Combine(dir, "dollars2-.log");
        // A port nothing is listening on: exercises the sink-added path without a running ES.
        const string unreachableEs = "http://127.0.0.1:59200";

        using (var logger = new LoggerConfiguration()
            .ConfigureDollars2Logging(pathTemplate, unreachableEs)
            .CreateLogger())
        {
            logger.Information("sprint-elasticsearch marker {Value}", 7);
        }

        try
        {
            var written = Directory.GetFiles(dir);
            Assert.Single(written);
            var content = File.ReadAllText(written[0]);
            Assert.Contains("sprint-elasticsearch marker 7", content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
