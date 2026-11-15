using System.Net;
using System.Net.Http.Json;
using Gebeta.Accounting.Contracts;
using Gebeta.Core.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>Credit notes, cancelled payments, opening invoices at cut-over and the ageing report.</summary>
public class InvoiceCorrectionsTests(GebetaApiFactory factory) : IClassFixture<GebetaApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task Credit_notes_and_cancelled_payments_flow_through_the_ledger_tax_reports_and_ageing()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Corrections PLC", abbreviation = "COR", legalForm = "PrivateLimitedCompany", tin = "0800000051", vatRegistrationNumber = "VAT-51" }, Ct))
            .Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var id = company!.Id;
        var customer = await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = id, name = "Bole Builders", type = "Customer", tin = "0800000052" }, Ct))
            .Content.ReadFromJsonAsync<PartyDto>(Ct);
        var supplier = await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = id, name = "Old Supplier", type = "Supplier" }, Ct))
            .Content.ReadFromJsonAsync<PartyDto>(Ct);

        var sale = await (await admin.PostAsJsonAsync(U("/api/accounting/invoices"), new
        {
            kind = "Sales", companyId = id, partyId = customer!.Id, postingDate = "2026-11-20", customerWithholds = true,
            lines = new object[] { new { description = "Cement", quantity = 2, rate = 10_000, accountCode = "4100", isService = false } },
        }, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        sale = await (await admin.PostAsync(U($"/api/accounting/invoices/{sale!.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);

        // Partial credit note: one bag back.
        var note = await (await admin.PostAsJsonAsync(U($"/api/accounting/invoices/{sale!.Id}/return"), new
        {
            postingDate = "2026-11-22", lines = new object[] { new { description = "Cement (returned)", quantity = 1, rate = 10_000, accountCode = "4100", isService = false } },
        }, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        Assert.True(note!.IsReturn);
        Assert.Equal(sale.Number, note.ReturnAgainstNumber);
        Assert.Equal(11_300m, note.PayableAmount);
        note = await (await admin.PostAsync(U($"/api/accounting/invoices/{note.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        Assert.Equal("CN-2019-00001", note!.Number);
        sale = await admin.GetFromJsonAsync<InvoiceDto>(U($"/api/accounting/invoices/{sale.Id}"), Ct);
        Assert.Equal(11_300m, sale!.Outstanding);

        // A full return is now more than is owed; an account not on the invoice is refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U($"/api/accounting/invoices/{sale.Id}/return"), new { postingDate = "2026-11-23" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U($"/api/accounting/invoices/{sale.Id}/return"), new
        {
            postingDate = "2026-11-23", lines = new object[] { new { description = "X", quantity = 1, rate = 1, accountCode = "4200", isService = false } },
        }, Ct)).StatusCode);

        // Pay the rest, then cancel the payment.
        var payment = await (await admin.PostAsJsonAsync(U("/api/accounting/payments"), new { invoiceId = sale.Id, amount = 11_300, postingDate = "2026-11-25" }, Ct))
            .Content.ReadFromJsonAsync<PaymentDto>(Ct);
        Assert.Equal(0m, (await admin.GetFromJsonAsync<InvoiceDto>(U($"/api/accounting/invoices/{sale.Id}"), Ct))!.Outstanding);
        var cancelled = await (await admin.PostAsync(U($"/api/accounting/payments/{payment!.Id}/cancel"), null, Ct)).Content.ReadFromJsonAsync<PaymentDto>(Ct);
        Assert.Equal("Cancelled", cancelled!.Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(U($"/api/accounting/payments/{payment.Id}/cancel"), null, Ct)).StatusCode);
        Assert.Equal(11_300m, (await admin.GetFromJsonAsync<InvoiceDto>(U($"/api/accounting/invoices/{sale.Id}"), Ct))!.Outstanding);
        Assert.Equal("Cancelled", Assert.Single((await admin.GetFromJsonAsync<List<PaymentDto>>(U($"/api/accounting/invoices/{sale.Id}/payments"), Ct))!).Status);

        // Ledger and tax reports net the credit note.
        var tb = await admin.GetFromJsonAsync<TrialBalanceDto>(U($"/api/accounting/reports/trial-balance?companyId={id}&asOf=2026-11-30"), Ct);
        decimal Balance(string code) => tb!.Rows.Single(r => r.Code == code).Balance;
        Assert.Equal(11_300m, Balance("1200"));
        Assert.Equal(10_000m, Balance("4100"));
        Assert.Equal(1_500m, Balance("2300"));
        Assert.Equal(200m, Balance("1260"));
        Assert.DoesNotContain(tb!.Rows, r => r.Code == "1120"); // payment and its reversal net to zero
        var vat = await admin.GetFromJsonAsync<VatReportDto>(U($"/api/accounting/reports/vat?companyId={id}&from=2026-11-01&to=2026-11-30"), Ct);
        Assert.Equal(1_500m, vat!.OutputVat);
        Assert.Contains(vat.Rows, r => r.Number == note.Number && r.Vat == -1_500m);
        var wht = await admin.GetFromJsonAsync<WithholdingReportDto>(U($"/api/accounting/reports/withholding?companyId={id}&from=2026-11-01&to=2026-11-30"), Ct);
        Assert.Equal(200m, wht!.TotalWithheldByCustomers);

        // Cut-over: a supplier invoice still open from the old system posts nothing; paying part of it does.
        var opening = await (await admin.PostAsJsonAsync(U("/api/accounting/invoices/opening"), new
        {
            kind = "Purchase", companyId = id, partyId = supplier!.Id, postingDate = "2026-07-07", dueDate = "2026-06-30", amount = 7_000, reference = "OLD-778",
        }, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        Assert.True(opening!.IsOpening);
        Assert.Equal("Submitted", opening.Status);
        Assert.Equal(7_000m, opening.Outstanding);
        await admin.PostAsJsonAsync(U("/api/accounting/payments"), new { invoiceId = opening.Id, amount = 3_000, postingDate = "2026-11-26" }, Ct);
        tb = await admin.GetFromJsonAsync<TrialBalanceDto>(U($"/api/accounting/reports/trial-balance?companyId={id}&asOf=2026-11-30"), Ct);
        Assert.Equal(-3_000m, tb!.Rows.Single(r => r.Code == "2100").Balance); // only the payment; the 7,000 is in the opening balances journal

        // Ageing on 2026-11-30.
        var receivables = await admin.GetFromJsonAsync<AgeingReportDto>(U($"/api/accounting/reports/ageing?companyId={id}&kind=Sales&asOf=2026-11-30"), Ct);
        var row = Assert.Single(receivables!.Rows);
        Assert.Equal(11_300m, row.NotDue); // due 2026-12-20
        var payables = await admin.GetFromJsonAsync<AgeingReportDto>(U($"/api/accounting/reports/ageing?companyId={id}&kind=Purchase&asOf=2026-11-30"), Ct);
        Assert.Equal(4_000m, Assert.Single(payables!.Rows).Over90); // due 2026-06-30
        Assert.Equal(4_000m, payables.Totals.Total);

        // Cancelling the credit note puts the amount back; with no payments or returns left, the invoice itself can be cancelled.
        await admin.PostAsync(U($"/api/accounting/invoices/{note.Id}/cancel"), null, Ct);
        Assert.Equal(22_600m, (await admin.GetFromJsonAsync<InvoiceDto>(U($"/api/accounting/invoices/{sale.Id}"), Ct))!.Outstanding);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(U($"/api/accounting/invoices/{sale.Id}/cancel"), null, Ct)).StatusCode);
    }
}
