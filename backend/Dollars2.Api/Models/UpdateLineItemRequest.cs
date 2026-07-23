using System.ComponentModel.DataAnnotations;

namespace Dollars2.Api.Models;

public class UpdateLineItemRequest
{
    [Required]
    [MaxLength(256)]
    public required string Name { get; set; }

    [Range(0, double.MaxValue)]
    public decimal PlannedAmount { get; set; }

    public string? Notes { get; set; }
}
