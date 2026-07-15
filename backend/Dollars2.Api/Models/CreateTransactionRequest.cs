using System.ComponentModel.DataAnnotations;

namespace Dollars2.Api.Models;

public class CreateTransactionRequest
{
    [Required]
    public DateTime Date { get; set; }

    [Required]
    [MinLength(1)]
    public string Description { get; set; } = "";

    [Required]
    public decimal Amount { get; set; }

    public string? Notes { get; set; }

    public string? Payee { get; set; }

    public string? Memo { get; set; }
}
