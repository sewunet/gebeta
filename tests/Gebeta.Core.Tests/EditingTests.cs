using Gebeta.Core.Domain.Companies;
using Gebeta.Core.Domain.Employees;
using Gebeta.Core.Domain.Parties;

namespace Gebeta.Core.Tests;

public class EditingTests
{
    private static readonly Guid CompanyId = Guid.CreateVersion7();
    private static readonly DateOnly Joined = new(2024, 1, 15);

    private static Employee NewEmployee() => Employee.Create(CompanyId, "EMP-00001", "Almaz", "Tesfaye", null, Gender.Female, Joined, null).Value;

    [Fact]
    public void An_employee_can_be_corrected_but_keeps_the_number()
    {
        var e = NewEmployee();
        Assert.True(e.Update("Almaz", "Tesfaye", "Bekele", Gender.Female, Joined.AddDays(-5), "0012345678").IsSuccess);
        Assert.Equal("Almaz Tesfaye Bekele", e.FullName);
        Assert.Equal("0012345678", e.Tin?.Value);
        Assert.Equal("EMP-00001", e.Number);
        Assert.True(e.Update("", "Tesfaye", null, Gender.Female, Joined, null).IsFailure);
        Assert.True(e.Update("Almaz", "Tesfaye", null, Gender.Female, Joined, "12").IsFailure);
        Assert.Equal("Almaz Tesfaye Bekele", e.FullName); // a refused change leaves the record as it was
    }

    [Fact]
    public void Leaving_once_and_taking_back_a_mistake()
    {
        var e = NewEmployee();
        Assert.True(e.Leave(Joined.AddDays(-1)).IsFailure);
        Assert.True(e.Leave(new DateOnly(2026, 10, 31)).IsSuccess);
        Assert.Equal(EmployeeStatus.Left, e.Status);
        Assert.True(e.Leave(new DateOnly(2026, 11, 30)).IsFailure);
        Assert.True(e.Update("Almaz", "Tesfaye", null, Gender.Female, new DateOnly(2026, 11, 15), null).IsFailure); // joined after leaving

        Assert.True(e.Rejoin().IsSuccess);
        Assert.Equal(EmployeeStatus.Active, e.Status);
        Assert.Null(e.DateOfLeaving);
        Assert.True(e.Rejoin().IsFailure);
    }

    [Fact]
    public void A_company_changes_name_and_vat_registration_but_not_tin_or_abbreviation()
    {
        var company = Company.Create("Abay Systems", "ABS", LegalForm.PrivateLimitedCompany, "0012345678").Value;
        Assert.False(company.IsVatRegistered);
        Assert.True(company.Update("Abay Systems PLC", "VAT-123", "AA/14/2019").IsSuccess);
        Assert.True(company.IsVatRegistered);
        Assert.Equal("Abay Systems PLC", company.Name);
        Assert.Equal("ABS", company.Abbreviation);
        Assert.True(company.Update(" ", null, null).IsFailure);
        Assert.True(company.Update("Abay Systems PLC", null, null).IsSuccess);
        Assert.False(company.IsVatRegistered);
    }

    [Fact]
    public void A_party_is_edited_with_the_same_checks_as_when_created()
    {
        var party = Party.Create(CompanyId, "Merkato Traders", PartyType.Customer, null, null, null).Value;
        Assert.True(party.Update("Merkato Traders PLC", PartyType.Both, "0800000012", "VAT-12", "+251911000000").IsSuccess);
        Assert.Equal(PartyType.Both, party.Type);
        Assert.True(party.IsVatRegistered);
        Assert.True(party.Update("Merkato Traders PLC", PartyType.Both, "bad", null, null).IsFailure);
        Assert.Equal("0800000012", party.Tin?.Value);
    }
}
