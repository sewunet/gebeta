using System.Net;
using System.Net.Http.Json;
using Gebeta.Accounting.Contracts;
using Gebeta.Core.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>Customers and suppliers, invoices with Ethiopian VAT and withholding, payments, and the tax reports.</summary>
public class InvoicingFlowTests(GebetaApiFactory factory) : IClassFixture<GebetaApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task Invoices_post_vat_and_withholding_and_payments_settle_them()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Invoice Test PLC", abbreviation = "INV", legalForm = "PrivateLimitedCompany", tin = "0800000011", vatRegistrationNumber = "VAT-11" }, Ct))
            .Content.ReadFromJsonAsync<CompanyDto>(Ct);

        var customer = await (await admin.PostAsJsonAsync(U("/api/core/parties"),
            new { companyId = company!.Id, name = "Habesha Builders", type = "Customer", tin = "0800000012" }, Ct)).Content.ReadFromJsonAsync<PartyDto>(Ct);
        var supplier = await (await admin.PostAsJsonAsync(U("/api/core/parties"),
            new { companyId = company.Id, name = "Addis Consulting", type = "Supplier", tin = "0800000013", vatRegistrationNumber = "VAT-13" }, Ct)).Content.ReadFromJsonAsync<PartyDto>(Ct);
        var parties = await admin.GetFromJsonAsync<List<PartyDto>>(U($"/api/core/parties?companyId={company.Id}"), Ct);
        Assert.Equal(2, parties!.Count);

        // Sales: we are VAT-registered (15%); the customer withholds 2% on goods above ETB 10,000.
        var sale = await (await admin.PostAsJsonAsync(U("/api/accounting/invoices"), new
        {
            kind = "Sales", companyId = company.Id, partyId = customer!.Id, postingDate = "2026-11-20", customerWithholds = true,
            lines = new object[] { new { description = "Cement (bags of 50)", quantity = 2, rate = 10_000, accountCode = "4100", isService = false } },
        }, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        Assert.Equal(3_000m, sale!.VatAmount);
        Assert.Equal(400m, sale.WithholdingAmount);
        Assert.Equal(22_600m, sale.PayableAmount);
        sale = await (await admin.PostAsync(U($"/api/accounting/invoices/{sale.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        Assert.Equal("SINV-2019-00001", sale!.Number);

        // Purchase: the supplier is VAT-registered; we withhold 2% on services above ETB 3,000.
        var purchase = await (await admin.PostAsJsonAsync(U("/api/accounting/invoices"), new
        {
            kind = "Purchase", companyId = company.Id, partyId = supplier!.Id, postingDate = "2026-11-21", supplierInvoiceNumber = "AC-778",
            lines = new object[] { new { description = "Tax advisory", quantity = 1, rate = 5_000, accountCode = "6900", isService = true } },
        }, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        Assert.Equal(750m, purchase!.VatAmount);
        Assert.Equal(100m, purchase.WithholdingAmount);
        purchase = await (await admin.PostAsync(U($"/api/accounting/invoices/{purchase.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        Assert.Equal("PINV-2019-00001", purchase!.Number);

        // Payments.
        var overpay = await admin.PostAsJsonAsync(U("/api/accounting/payments"), new { invoiceId = sale.Id, amount = 30_000, postingDate = "2026-11-25" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, overpay.StatusCode);
        var received = await (await admin.PostAsJsonAsync(U("/api/accounting/payments"),
            new { invoiceId = sale.Id, amount = 10_000, postingDate = "2026-11-25", reference = "CBE transfer" }, Ct)).Content.ReadFromJsonAsync<PaymentDto>(Ct);
        Assert.Equal("Receive", received!.Kind);
        Assert.Equal("PAY-2019-00001", received.Number);
        await admin.PostAsJsonAsync(U("/api/accounting/payments"), new { invoiceId = purchase.Id, amount = 5_650, postingDate = "2026-11-26" }, Ct);

        sale = await admin.GetFromJsonAsync<InvoiceDto>(U($"/api/accounting/invoices/{sale.Id}"), Ct);
        Assert.Equal(12_600m, sale!.Outstanding);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(U($"/api/accounting/invoices/{sale.Id}/cancel"), null, Ct)).StatusCode);

        var tb = await admin.GetFromJsonAsync<TrialBalanceDto>(U($"/api/accounting/reports/trial-balance?companyId={company.Id}&asOf=2026-11-30"), Ct);
        decimal Balance(string code) => tb!.Rows.Single(r => r.Code == code).Balance;
        Assert.Equal(12_600m, Balance("1200"));   // receivable left
        Assert.Equal(400m, Balance("1260"));      // withholding to claim
        Assert.Equal(750m, Balance("1250"));      // input VAT
        Assert.Equal(20_000m, Balance("4100"));   // sales (credit-normal accounts show positive)
        Assert.Equal(3_000m, Balance("2300"));    // output VAT
        Assert.Equal(100m, Balance("2400"));      // withholding to pay over
        Assert.Equal(5_000m, Balance("6900"));
        Assert.Equal(4_350m, Balance("1120"));    // bank: 10,000 in, 5,650 out
        Assert.DoesNotContain(tb!.Rows, r => r.Code == "2100"); // supplier fully paid
        Assert.Equal(tb.TotalDebit, tb.TotalCredit);

        var vat = await admin.GetFromJsonAsync<VatReportDto>(U($"/api/accounting/reports/vat?companyId={company.Id}&from=2026-11-01&to=2026-11-30"), Ct);
        Assert.Equal(3_000m, vat!.OutputVat);
        Assert.Equal(750m, vat.InputVat);
        Assert.Equal(2_250m, vat.NetPayable);
        Assert.Equal(2, vat.Rows.Count);

        var wht = await admin.GetFromJsonAsync<WithholdingReportDto>(U($"/api/accounting/reports/withholding?companyId={company.Id}&from=2026-11-01&to=2026-11-30"), Ct);
        Assert.Equal(100m, wht!.TotalWithheldFromSuppliers);
        Assert.Equal(400m, wht.TotalWithheldByCustomers);
        Assert.Equal("0800000013", Assert.Single(wht.FromSuppliers).PartyTin);
    }

    [Fact]
    public async Task A_draft_invoice_can_be_cancelled_without_touching_the_ledger_and_small_purchases_have_no_withholding()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Small Shop PLC", abbreviation = "SSP", legalForm = "PrivateLimitedCompany", tin = "0800000021" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var supplier = await (await admin.PostAsJsonAsync(U("/api/core/parties"),
            new { companyId = company!.Id, name = "Merkato Supplies", type = "Supplier" }, Ct)).Content.ReadFromJsonAsync<PartyDto>(Ct);

        var purchase = await (await admin.PostAsJsonAsync(U("/api/accounting/invoices"), new
        {
            kind = "Purchase", companyId = company.Id, partyId = supplier!.Id, postingDate = "2026-11-21",
            lines = new object[] { new { description = "Shelving", quantity = 1, rate = 8_000, accountCode = "1500", isService = false } },
        }, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        Assert.Equal(0m, purchase!.VatAmount);         // supplier not VAT-registered
        Assert.Equal(0m, purchase.WithholdingAmount);  // goods under ETB 10,000

        purchase = await (await admin.PostAsync(U($"/api/accounting/invoices/{purchase.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        purchase = await (await admin.PostAsync(U($"/api/accounting/invoices/{purchase!.Id}/cancel"), null, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        Assert.Equal("Cancelled", purchase!.Status);
        var tb = await admin.GetFromJsonAsync<TrialBalanceDto>(U($"/api/accounting/reports/trial-balance?companyId={company.Id}&asOf=2026-11-30"), Ct);
        Assert.Empty(tb!.Rows);

        var badAccount = await admin.PostAsJsonAsync(U("/api/accounting/invoices"), new
        {
            kind = "Purchase", companyId = company.Id, partyId = supplier.Id, postingDate = "2026-11-21",
            lines = new object[] { new { description = "X", quantity = 1, rate = 1, accountCode = "6000", isService = false } },
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, badAccount.StatusCode);
    }
}
