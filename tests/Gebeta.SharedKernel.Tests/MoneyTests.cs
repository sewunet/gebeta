using Gebeta.SharedKernel;

namespace Gebeta.SharedKernel.Tests;

public class MoneyTests
{
    [Fact]
    public void Adds_amounts_of_the_same_currency_exactly()
    {
        var total = new Money(0.1m, "ETB") + new Money(0.2m, "ETB");

        Assert.Equal(new Money(0.3m, "ETB"), total);
    }

    [Fact]
    public void Refuses_to_mix_currencies()
    {
        var ex = Assert.Throws<CurrencyMismatchException>(() => new Money(1m, "ETB") + new Money(1m, "USD"));

        Assert.Equal("money.currency_mismatch", ex.Code);
    }

    [Theory]
    [InlineData("2.345", "2.35")]
    [InlineData("-2.345", "-2.35")]
    [InlineData("2.344", "2.34")]
    [InlineData("1000.005", "1000.01")]
    public void Rounds_halves_away_from_zero(string input, string expected)
    {
        var rounded = new Money(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture), "ETB").Round();

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), rounded.Amount);
    }

    [Fact]
    public void Normalizes_the_currency_code()
    {
        Assert.Equal("ETB", new Money(5m, " etb ").Currency);
    }

    [Theory]
    [InlineData("")]
    [InlineData("BIRR")]
    [InlineData("E1B")]
    public void Rejects_invalid_currency_codes(string currency)
    {
        Assert.Throws<ArgumentException>(() => new Money(1m, currency));
    }

    [Fact]
    public void Sums_a_list_and_compares()
    {
        var lines = new[] { new Money(1500m, "ETB"), new Money(2499.99m, "ETB"), new Money(0.01m, "ETB") };

        var total = Money.Sum(lines, "ETB");

        Assert.Equal(4000m, total.Amount);
        Assert.True(total > new Money(3999.99m, "ETB"));
    }

    [Fact]
    public void Pension_seven_percent_of_basic_salary_rounds_to_the_cent()
    {
        var pension = (new Money(13_333.33m, "ETB") * 0.07m).Round();

        Assert.Equal(933.33m, pension.Amount);
    }
}
