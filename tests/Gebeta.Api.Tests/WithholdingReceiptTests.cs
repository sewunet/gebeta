using System.Net;
using System.Net.Http.Json;
using Gebeta.Accounting.Contracts;
using Gebeta.Core.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>Withholding receipts for suppliers and the VAT and withholding declaration files.</summary>
public class WithholdingReceiptTests(GebetaApiFactory factory) : IClassFixture<GebetaApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task A_purchase_with_withholding_gets_a_numbered_receipt_and_both_declarations_download_as_csv()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Agent PLC", abbreviation = "AGT", legalForm = "PrivateLimitedCompany", tin = "0800000061", vatRegistrationNumber = "VAT-61" }, Ct))
            .Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var id = company!.Id;
        var supplier = await (await admin.PostAsJsonAsync(U("/api/core/parties"),
            new { companyId = id, name = "Piassa Printing, \"PP\"", type = "Supplier", tin = "0800000062", vatRegistrationNumber = "VAT-62" }, Ct)).Content.ReadFromJsonAsync<PartyDto>(Ct);
        var customer = await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = id, name = "Kazanchis Hotel", type = "Customer" }, Ct))
            .Content.ReadFromJsonAsync<PartyDto>(Ct);

        var purchase = await (await admin.PostAsJsonAsync(U("/api/accounting/invoices"), new
        {
            kind = "Purchase", companyId = id, partyId = supplier!.Id, postingDate = "2026-11-21", supplierInvoiceNumber = "PP-0042",
            lines = new object[]
            {
                new { description = "Paper", quantity = 1, rate = 8_000, accountCode = "6900", isService = false },
                new { description = "Printing", quantity = 1, rate = 5_000, accountCode = "6900", isService = true },
            },
        }, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        Assert.Null(purchase!.WithholdingReceiptNumber);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(U($"/api/accounting/invoices/{purchase.Id}/withholding-receipt"), Ct)).StatusCode);

        purchase = await (await admin.PostAsync(U($"/api/accounting/invoices/{purchase.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        Assert.Equal("WHT-2019-00001", purchase!.WithholdingReceiptNumber);

        var receipt = await admin.GetFromJsonAsync<WithholdingReceiptDto>(U($"/api/accounting/invoices/{purchase.Id}/withholding-receipt"), Ct);
        Assert.Equal("Agent PLC", receipt!.AgentName);
        Assert.Equal("0800000061", receipt.AgentTin);
        Assert.Equal("0800000062", receipt.SupplierTin);
        Assert.Equal("PP-0042", receipt.SupplierInvoiceNumber);
        Assert.Equal("Goods and services", receipt.SupplyType);
        Assert.Equal(5_000m, receipt.TaxableAmount); // the paper (ETB 8,000) is under the goods threshold
        Assert.Equal(3m, receipt.RatePercent);
        Assert.Equal(150m, receipt.Withheld);

        var sale = await (await admin.PostAsJsonAsync(U("/api/accounting/invoices"), new
        {
            kind = "Sales", companyId = id, partyId = customer!.Id, postingDate = "2026-11-22",
            lines = new object[] { new { description = "Banner", quantity = 1, rate = 2_000, accountCode = "4100", isService = false } },
        }, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        sale = await (await admin.PostAsync(U($"/api/accounting/invoices/{sale!.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        Assert.Null(sale!.WithholdingReceiptNumber);

        var wht = await admin.GetAsync(U($"/api/accounting/reports/withholding?companyId={id}&from=2026-11-01&to=2026-11-30&format=csv"), Ct);
        Assert.Equal("text/csv", wht.Content.Headers.ContentType!.MediaType);
        var whtLines = (await wht.Content.ReadAsStringAsync(Ct)).TrimStart('\uFEFF').Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Receipt No,Date,Supplier Name,Supplier TIN", whtLines[0], StringComparison.Ordinal);
        Assert.Equal("WHT-2019-00001,2026-11-21,\"Piassa Printing, \"\"PP\"\"\",0800000062,PINV-2019-00001,Goods and services,5000.00,3.00,150.00", whtLines[1]);
        Assert.Equal("Total,,,,,,5000.00,,150.00", whtLines[2]);

        var vat = await (await admin.GetAsync(U($"/api/accounting/reports/vat?companyId={id}&from=2026-11-01&to=2026-11-30&format=csv"), Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Output (sales),SINV-2019-00001,2026-11-22,Kazanchis Hotel,,2000.00,300.00", vat, StringComparison.Ordinal);
        Assert.Contains("Input (purchases),PINV-2019-00001", vat, StringComparison.Ordinal);
        Assert.Contains("Net VAT payable,,,,,,-1650.00", vat, StringComparison.Ordinal); // 300 output − 1,950 input
    }
}
