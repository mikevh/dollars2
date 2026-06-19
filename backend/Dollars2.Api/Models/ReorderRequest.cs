using System.ComponentModel.DataAnnotations;

namespace Dollars2.Api.Models;

public class ReorderRequest
{
    [Required]
    public required int[] Ids { get; set; }
}
