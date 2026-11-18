using Gebeta.SharedKernel.Ethiopia;

namespace Gebeta.SharedKernel.Tests;

/// <summary>Amounts in words for vouchers: the boundaries and the worked examples of docs/requirements/competitive-features.md #2.</summary>
public class AmountInWordsTests
{
    [Theory]
    [InlineData("0", "Zero birr only")]
    [InlineData("0.01", "One santim only")]
    [InlineData("1", "One birr only")]
    [InlineData("10", "Ten birr only")]
    [InlineData("11", "Eleven birr only")]
    [InlineData("19", "Nineteen birr only")]
    [InlineData("20", "Twenty birr only")]
    [InlineData("99", "Ninety-nine birr only")]
    [InlineData("100", "One hundred birr only")]
    [InlineData("101", "One hundred one birr only")]
    [InlineData("999", "Nine hundred ninety-nine birr only")]
    [InlineData("1000", "One thousand birr only")]
    [InlineData("1001", "One thousand one birr only")]
    [InlineData("10000", "Ten thousand birr only")]
    [InlineData("100000", "One hundred thousand birr only")]
    [InlineData("999999.99", "Nine hundred ninety-nine thousand nine hundred ninety-nine birr and ninety-nine santim only")]
    [InlineData("1000000", "One million birr only")]
    [InlineData("1000000000", "One billion birr only")]
    [InlineData("999999999999.99", "Nine hundred ninety-nine billion nine hundred ninety-nine million nine hundred ninety-nine thousand nine hundred ninety-nine birr and ninety-nine santim only")]
    // The worked examples.
    [InlineData("112000", "One hundred twelve thousand birr only")]
    [InlineData("2475.25", "Two thousand four hundred seventy-five birr and twenty-five santim only")]
    [InlineData("33740", "Thirty-three thousand seven hundred forty birr only")]
    [InlineData("1150000.50", "One million one hundred fifty thousand birr and fifty santim only")]
    [InlineData("0.75", "Seventy-five santim only")]
    public void English(string amount, string expected) => Assert.Equal(expected, AmountInWords.English(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("0", "ዜሮ ብር")]
    [InlineData("0.01", "አንድ ሳንቲም")]
    [InlineData("10", "አሥር ብር")]
    [InlineData("11", "አሥራ አንድ ብር")]
    [InlineData("19", "አሥራ ዘጠኝ ብር")]
    [InlineData("20", "ሃያ ብር")]
    [InlineData("99", "ዘጠና ዘጠኝ ብር")]
    [InlineData("100", "አንድ መቶ ብር")]
    [InlineData("1000", "አንድ ሺህ ብር")]
    [InlineData("1000000000", "አንድ ቢሊዮን ብር")]
    // The worked examples.
    [InlineData("112000", "አንድ መቶ አሥራ ሁለት ሺህ ብር")]
    [InlineData("2475.25", "ሁለት ሺህ አራት መቶ ሰባ አምስት ብር ከሃያ አምስት ሳንቲም")]
    [InlineData("33740", "ሠላሳ ሦስት ሺህ ሰባት መቶ አርባ ብር")]
    [InlineData("1150000.50", "አንድ ሚሊዮን አንድ መቶ ሃምሳ ሺህ ብር ከሃምሳ ሳንቲም")]
    [InlineData("0.75", "ሰባ አምስት ሳንቲም")]
    public void Amharic(string amount, string expected) => Assert.Equal(expected, AmountInWords.Amharic(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Negative_amounts_are_written_without_minus_and_rounding_follows_the_documents()
    {
        Assert.Equal("Five hundred birr only", AmountInWords.English(-500m));
        Assert.Equal("Two birr and thirty-five santim only", AmountInWords.English(2.345m)); // half away from zero, like the documents
        Assert.Equal("አምስት መቶ ብር ብቻ", AmountInWords.Amharic(500m, withOnly: true));
    }

    [Fact]
    public void Foreign_currencies_use_their_own_names_and_too_large_amounts_are_refused()
    {
        Assert.Equal("One hundred dollars and fifty cents only", AmountInWords.English(100.50m, "USD"));
        Assert.Equal("አንድ መቶ ዶላር ከሃምሳ ሳንቲም", AmountInWords.Amharic(100.50m, "USD"));
        Assert.Throws<ArgumentOutOfRangeException>(() => AmountInWords.English(1_000_000_000_000m));
    }
}
