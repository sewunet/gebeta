using Gebeta.Core.Domain.Companies;

namespace Gebeta.Core.Tests;

public class CompanyTests
{
    [Fact]
    public void A_new_company_uses_ethiopian_defaults()
    {
        var company = Company.Create("Ethiopian Sugar Industry Group", "esig", LegalForm.PublicEnterprise, "0000123456").Value;

        Assert.Equal("ESIG", company.Abbreviation);
        Assert.Equal("ETB", company.DefaultCurrencyCode);
        Assert.Equal(FiscalYearType.Ethiopian, company.FiscalYearType);
        Assert.Equal("Africa/Addis_Ababa", company.TimeZone);
        Assert.False(company.IsVatRegistered);
    }

    [Fact]
    public void The_fiscal_year_runs_hamle_to_sene_by_default()
    {
        var company = Company.Create("Abay Systems", "ABS", LegalForm.PrivateLimitedCompany, "0012345678").Value;

        var (start, end) = company.FiscalYearOf(new DateOnly(2026, 11, 14));

        Assert.Equal(new DateOnly(2026, 7, 8), start);
        Assert.Equal(new DateOnly(2027, 7, 7), end);
    }

    [Fact]
    public void A_company_can_use_the_gregorian_fiscal_year()
    {
        var company = Company.Create("Foreign Branch", "FB", LegalForm.ForeignBranch, "0012345679").Value;
        company.UseGregorianFiscalYear();

        Assert.Equal((new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), company.FiscalYearOf(new DateOnly(2026, 11, 14)));
    }

    [Fact]
    public void A_vat_registration_number_makes_the_company_vat_registered()
    {
        var company = Company.Create("Guba", "GT", LegalForm.PrivateLimitedCompany, "0012345670", vatRegistrationNumber: "12345678").Value;

        Assert.True(company.IsVatRegistered);
    }

    [Theory]
    [InlineData("", "ABC", "0012345678")]
    [InlineData("Name", "A", "0012345678")]
    [InlineData("Name", "AB-C", "0012345678")]
    [InlineData("Name", "ABC", "12345")]
    public void Invalid_input_is_refused(string name, string abbreviation, string tin)
    {
        var result = Company.Create(name, abbreviation, LegalForm.ShareCompany, tin);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Code);
    }
}
