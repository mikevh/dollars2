using System.ComponentModel.DataAnnotations;

namespace Dollars2.Api.Models;

public class CreateBudgetRequest
{
    [Required]
    [Range(2000, 2100)]
    public int Year { get; set; }

    [Required]
    [Range(1, 12)]
    public int Month { get; set; }
}
