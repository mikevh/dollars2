using System.ComponentModel.DataAnnotations;

namespace Dollars2.Api.Models;

public class CreateTransactionRequest
{
    [Required]
    public DateOnly Date { get; set; }

    [Required]
    [MinLength(1)]
    [MaxLength(TransactionText.MaxLength)]
    public string Description { get; set; } = "";

    [Required]
    public decimal Amount { get; set; }

    public string? Notes { get; set; }

    [MaxLength(TransactionText.MaxLength)]
    public string? Payee { get; set; }

    [MaxLength(TransactionText.MaxLength)]
    public string? Memo { get; set; }
}
