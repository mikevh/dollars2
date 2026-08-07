namespace Dollars2.Api.Models;

/// <summary>
/// One archived sighting of a provider transaction — what the provider said about it during one sync
/// run. A transaction seen across several syncs has one of these per sighting.
/// </summary>
public class RawHistoryEntryResponse
{
    public DateTime SyncedAt { get; set; }

    /// <summary>The provider the payload came from, e.g. <c>SimpleFIN</c> or <c>Plaid</c>.</summary>
    public string SourceType { get; set; } = "";

    /// <summary>Shared by every item archived by the same sync run, so a run can be reassembled.</summary>
    public string SyncRunId { get; set; } = "";

    /// <summary>
    /// The provider's payload, verbatim. Deliberately a string: re-parsing and re-serializing it here
    /// would throw away the exact bytes that are the whole reason the archive exists. Clients
    /// pretty-print it themselves.
    /// </summary>
    public string RawJson { get; set; } = "";
}
