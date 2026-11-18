using System.Net.Http.Json;
using Gebeta.Accounting.Contracts;
using Gebeta.Core.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>Statements of account for a customer and a supplier (docs/requirements/competitive-features.md #4).</summary>
public class PartyStatementTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task A_customer_statement_runs_from_the_opening_balance_through_withholding_returns_and_payments_to_the_ageing_total()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Statement PLC", Ct);
        var customer = (await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = company.Id, name = "Bole Hotel", type = "Customer", tin = TestData.Tin() }, Ct))
            .Content.ReadFromJsonAsync<PartyDto>(Ct))!;

        async Task<InvoiceDto> Submit(HttpResponseMessage created)
        {
            var draft = (await created.Content.ReadFromJsonAsync<InvoiceDto>(Ct))!;
            return (await (await admin.PostAsync(U($"/api/accounting/invoices/{draft.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct))!;
        }

        async Task<InvoiceDto> Sell(string date, int quantity) => await Submit(await admin.PostAsJsonAsync(U("/api/accounting/invoices"), new
        {
            kind = "Sales", companyId = company.Id, partyId = customer.Id, postingDate = date, customerWithholds = true,
            lines = new object[] { new { description = "Cement", quantity, rate = 10_000, accountCode = "4100", isService = false } },
        }, Ct));

        async Task<PaymentDto> Receive(Guid invoiceId, decimal amount, string date) =>
            (await (await admin.PostAsJsonAsync(U("/api/accounting/payments"), new { invoiceId, amount, postingDate = date, reference = "CBE" }, Ct)).Content.ReadFromJsonAsync<PaymentDto>(Ct))!;

        // Before the statement: 20,000 sold, 600 withheld, 5,000 received → 14,400 opening.
        var october = await Sell("2026-10-15", 2);
        await Receive(october.Id, 5_000m, "2026-10-20");
        // In range: 30,000 sold (900 withheld), one bag of October's back (10,000 less the 300 withheld on it), October's rest paid.
        var november = await Sell("2026-11-05", 3);
        await Submit(await admin.PostAsJsonAsync(U($"/api/accounting/invoices/{october.Id}/return"), new
        {
            postingDate = "2026-11-08", lines = new object[] { new { description = "Cement (returned)", quantity = 1, rate = 10_000, accountCode = "4100", isService = false } },
        }, Ct));
        await Receive(october.Id, 4_700m, "2026-11-12");
        var cancelled = await Receive(november.Id, 5_000m, "2026-11-13");
        (await admin.PostAsync(U($"/api/accounting/payments/{cancelled.Id}/cancel"), null, Ct)).EnsureSuccessStatusCode();
        await Sell("2026-12-01", 1); // after the statement

        var s = (await admin.GetFromJsonAsync<PartyStatementDto>(
            U($"/api/accounting/reports/party-statement?companyId={company.Id}&partyId={customer.Id}&kind=Sales&from=2026-11-01&to=2026-11-30"), Ct))!;

        Assert.Equal(14_400m, s.Opening);
        Assert.Equal(
            [("Invoice", 30_000m, 0m, 44_400m), ("Withholding", 0m, 900m, 43_500m), ("Credit note", 0m, 10_000m, 33_500m),
             ("Withholding reversed", 300m, 0m, 33_800m), ("Payment", 0m, 4_700m, 29_100m)],
            s.Lines.Select(l => (l.Type, l.Debit, l.Credit, l.Balance)));
        Assert.Equal((30_300m, 15_600m, 29_100m), (s.TotalDebit, s.TotalCredit, s.Closing));
        Assert.Equal(s.Closing, s.Ageing.Total); // the statement and the ageing never disagree
        Assert.Equal(october.Number, s.Lines.Single(l => l.Type == "Credit note").Reference);
        Assert.Equal(customer.Tin, s.PartyTin);

        var csv = await (await admin.GetAsync(
            U($"/api/accounting/reports/party-statement?companyId={company.Id}&partyId={customer.Id}&kind=Sales&from=2026-11-01&to=2026-11-30&format=csv"), Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Opening balance,,Bole Hotel,,,,14400.00", csv, StringComparison.Ordinal);
        Assert.Contains("Closing balance,,,,30300.00,15600.00,29100.00", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_supplier_statement_shows_what_we_owe_as_a_positive_balance_and_refuses_another_companys_party()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Supplier Statement PLC", Ct);
        var other = await admin.NewCompanyAsync("Other Statement PLC", Ct);
        var supplier = (await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = company.Id, name = "Merkato Services", type = "Supplier" }, Ct))
            .Content.ReadFromJsonAsync<PartyDto>(Ct))!;
        var draft = (await (await admin.PostAsJsonAsync(U("/api/accounting/invoices"), new
        {
            kind = "Purchase", companyId = company.Id, partyId = supplier.Id, postingDate = "2026-11-10",
            lines = new object[] { new { description = "Cleaning", quantity = 1, rate = 5_000, accountCode = "6900", isService = true } },
        }, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct))!;
        (await admin.PostAsync(U($"/api/accounting/invoices/{draft.Id}/submit"), null, Ct)).EnsureSuccessStatusCode();

        var s = (await admin.GetFromJsonAsync<PartyStatementDto>(
            U($"/api/accounting/reports/party-statement?companyId={company.Id}&partyId={supplier.Id}&kind=Purchase&from=2026-11-01&to=2026-11-30"), Ct))!;
        Assert.Equal([("Invoice", 0m, 5_000m, 5_000m), ("Withholding", 150m, 0m, 4_850m)], s.Lines.Select(l => (l.Type, l.Debit, l.Credit, l.Balance)));
        Assert.Equal(s.Closing, s.Ageing.Total);

        var foreign = await admin.GetAsync(
            U($"/api/accounting/reports/party-statement?companyId={other.Id}&partyId={supplier.Id}&kind=Purchase&from=2026-11-01&to=2026-11-30"), Ct);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, foreign.StatusCode);
    }
}
