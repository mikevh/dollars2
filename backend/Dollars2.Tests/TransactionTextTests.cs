using System.ComponentModel.DataAnnotations;
using Dollars2.Api.Models;

namespace Dollars2.Tests;

/// <summary>
/// Description, Payee and Memo land in nvarchar(500) columns, and SQL Server errors rather than
/// truncating when they overflow (issue #93). User-entered text is rejected at the request boundary;
/// provider text is clamped. These cover both halves of that split.
/// </summary>
public class TransactionTextTests
{
    private static string Text(int length) => new('x', length);

    [Fact]
    public void Truncate_leaves_a_value_the_column_can_hold_untouched()
    {
        var atLimit = Text(TransactionText.MaxLength);

        Assert.Same(atLimit, TransactionText.Truncate(atLimit));
        Assert.Equal("Coffee", TransactionText.Truncate("Coffee"));
        Assert.Equal("", TransactionText.Truncate(""));
    }

    [Fact]
    public void Truncate_clamps_an_over_length_value_to_its_first_500_characters()
    {
        var truncated = TransactionText.Truncate("ab" + Text(600));

        Assert.Equal(TransactionText.MaxLength, truncated.Length);
        Assert.StartsWith("ab", truncated);
    }

    // A provider DTO property is declared non-nullable, but System.Text.Json still writes null into it
    // for an explicit JSON null. Throwing here would fail the provider's whole fetch, and BankSyncService
    // fails every account on the connection when a fetch throws — strictly worse than the bug being fixed.
    [Fact]
    public void Truncate_normalizes_a_null_provider_value_to_empty()
    {
        Assert.Equal("", TransactionText.Truncate(null));
    }

    [Fact]
    public void Truncate_does_not_split_a_surrogate_pair_at_the_cut()
    {
        // Pad so that the 500-char boundary falls between the two halves of the emoji.
        var truncated = TransactionText.Truncate(Text(TransactionText.MaxLength - 1) + "\U0001F600" + Text(100));

        Assert.Equal(TransactionText.MaxLength - 1, truncated.Length);
        Assert.DoesNotContain(truncated, char.IsSurrogate);
    }

    [Fact]
    public void Truncate_keeps_a_surrogate_pair_that_fits_whole()
    {
        var truncated = TransactionText.Truncate(Text(TransactionText.MaxLength - 2) + "\U0001F600" + Text(100));

        Assert.Equal(TransactionText.MaxLength, truncated.Length);
        Assert.EndsWith("\U0001F600", truncated);
    }

    [Theory]
    [InlineData(TransactionText.MaxLength, true)]
    [InlineData(TransactionText.MaxLength + 1, false)]
    public void Create_request_accepts_a_description_only_up_to_the_column_width(int length, bool expectValid)
    {
        var request = new CreateTransactionRequest { Description = Text(length), Amount = -1m };

        Assert.Equal(expectValid, IsValid(request));
    }

    [Theory]
    [InlineData(TransactionText.MaxLength, true)]
    [InlineData(TransactionText.MaxLength + 1, false)]
    public void Create_request_accepts_a_payee_only_up_to_the_column_width(int length, bool expectValid)
    {
        var request = new CreateTransactionRequest { Description = "Coffee", Amount = -1m, Payee = Text(length) };

        Assert.Equal(expectValid, IsValid(request));
    }

    [Theory]
    [InlineData(TransactionText.MaxLength, true)]
    [InlineData(TransactionText.MaxLength + 1, false)]
    public void Create_request_accepts_a_memo_only_up_to_the_column_width(int length, bool expectValid)
    {
        var request = new CreateTransactionRequest { Description = "Coffee", Amount = -1m, Memo = Text(length) };

        Assert.Equal(expectValid, IsValid(request));
    }

    [Theory]
    [InlineData(TransactionText.MaxLength, true)]
    [InlineData(TransactionText.MaxLength + 1, false)]
    public void Update_request_accepts_a_description_only_up_to_the_column_width(int length, bool expectValid)
    {
        var request = new UpdateTransactionRequest { Description = Text(length), Amount = -1m };

        Assert.Equal(expectValid, IsValid(request));
    }

    // Mirrors what [ApiController] does before the action runs: a failing ModelState is the 400.
    private static bool IsValid(object request)
    {
        return Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), true);
    }
}
