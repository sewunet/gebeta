using System.Net;
using System.Net.Http.Json;
using Gebeta.Accounting.Contracts;
using Gebeta.Core.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>Editing companies, employees (including leaving) and customers or suppliers.</summary>
public class EditingApiTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task Records_are_edited_and_a_company_registered_for_vat_charges_vat_from_then_on()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Edit Me PLC", abbreviation = "EDT", legalForm = "PrivateLimitedCompany", tin = "0800000091" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var id = company!.Id;

        // Company
        var renamed = await (await admin.PutAsJsonAsync(U($"/api/core/companies/{id}"), new { name = "Edited PLC", vatRegistrationNumber = "VAT-91" }, Ct))
            .Content.ReadFromJsonAsync<CompanyDto>(Ct);
        Assert.Equal("Edited PLC", renamed!.Name);
        Assert.Equal("EDT", renamed.Abbreviation);
        Assert.Equal("VAT-91", renamed.VatRegistrationNumber);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(U($"/api/core/companies/{id}"), new { name = "" }, Ct)).StatusCode);

        // Customer
        var party = await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = id, name = "Piassa Shop", type = "Customer" }, Ct)).Content.ReadFromJsonAsync<PartyDto>(Ct);
        var edited = await (await admin.PutAsJsonAsync(U($"/api/core/parties/{party!.Id}"), new { name = "Piassa Shop PLC", type = "Both", tin = "0800000092", phone = "0911000000" }, Ct))
            .Content.ReadFromJsonAsync<PartyDto>(Ct);
        Assert.Equal("Both", edited!.Type);
        Assert.Equal("0800000092", (await admin.GetFromJsonAsync<PartyDto>(U($"/api/core/parties/{party.Id}"), Ct))!.Tin);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(U($"/api/core/parties/{party.Id}"), new { name = "X", type = "Customer", tin = "12" }, Ct)).StatusCode);

        // The company is now VAT-registered, so a sales invoice charges 15%.
        var invoice = await (await admin.PostAsJsonAsync(U("/api/accounting/invoices"), new
        {
            kind = "Sales", companyId = id, partyId = party.Id, postingDate = "2026-11-16",
            lines = new object[] { new { description = "Goods", quantity = 1, rate = 1_000, accountCode = "4100", isService = false } },
        }, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        Assert.Equal(150m, invoice!.VatAmount);
        Assert.Equal("Piassa Shop PLC", invoice.PartyName);

        // Employees
        var first = await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = id, firstName = "Abebe", fatherName = "Kebede", gender = "Male", dateOfJoining = "2025-01-01", tin = "0800000093" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct);
        var second = await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = id, firstName = "Sara", fatherName = "Alemu", gender = "Female", dateOfJoining = "2025-02-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct);
        var fixedName = await (await admin.PutAsJsonAsync(U($"/api/core/employees/{second!.Id}"),
            new { firstName = "Sara", fatherName = "Alemu", grandfatherName = "Tadesse", gender = "Female", dateOfJoining = "2025-02-01", tin = "0800000094" }, Ct))
            .Content.ReadFromJsonAsync<EmployeeDto>(Ct);
        Assert.Equal("Sara Alemu Tadesse", fixedName!.FullName);
        Assert.Equal(second.Number, fixedName.Number);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(U($"/api/core/employees/{second.Id}"),
            new { firstName = "Sara", fatherName = "Alemu", gender = "Female", dateOfJoining = "2025-02-01", tin = first!.Tin }, Ct)).StatusCode);

        var left = await (await admin.PostAsJsonAsync(U($"/api/core/employees/{first.Id}/leaving"), new { dateOfLeaving = "2026-10-31" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct);
        Assert.Equal("Left", left!.Status);
        Assert.Equal(new DateOnly(2026, 10, 31), left.DateOfLeaving);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U($"/api/core/employees/{first.Id}/leaving"), new { dateOfLeaving = "2026-11-30" }, Ct)).StatusCode);
        var back = await (await admin.PostAsJsonAsync(U($"/api/core/employees/{first.Id}/leaving"), new { dateOfLeaving = (string?)null }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct);
        Assert.Equal("Active", back!.Status);
        Assert.Null(back.DateOfLeaving);
    }
}
