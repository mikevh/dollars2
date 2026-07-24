using System.ComponentModel.DataAnnotations;

namespace Dollars2.Api.Models;

public class ResyncRequest
{
    /// <summary>
    /// Number of days to look back when re-fetching. Bounded 1–730; the UI defaults to 180.
    /// </summary>
    [Range(1, 730)]
    public int Days { get; set; } = 180;
}
