using System.Net;
using System.Net.Http.Json;
using Gebeta.Accounting.Contracts;
using Gebeta.Core.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>One payment for several invoices, and bank charges and interest booked from the statement.</summary>
public class PaymentAllocationTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task One_cheque_pays_two_invoices_and_bank_charges_are_booked_from_the_statement()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Allocation PLC", abbreviation = "ALC", legalForm = "PrivateLimitedCompany", tin = "0800000081" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var id = company!.Id;
        async Task<PartyDto> Party(string name) => (await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = id, name, type = "Customer" }, Ct))
            .Content.ReadFromJsonAsync<PartyDto>(Ct))!;
        async Task<InvoiceDto> Sale(PartyDto party, decimal amount)
        {
            var draft = await (await admin.PostAsJsonAsync(U("/api/accounting/invoices"), new
            {
                kind = "Sales", companyId = id, partyId = party.Id, postingDate = "2026-11-10",
                lines = new object[] { new { description = "Goods", quantity = 1, rate = amount, accountCode = "4100", isService = false } },
            }, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
            return (await (await admin.PostAsync(U($"/api/accounting/invoices/{draft!.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct))!;
        }

        var merkato = await Party("Merkato Traders");
        var other = await Party("Other Customer");
        var first = await Sale(merkato, 10_000m);
        var second = await Sale(merkato, 5_000m);
        var foreign = await Sale(other, 3_000m);

        var payment = await (await admin.PostAsJsonAsync(U("/api/accounting/payments"), new
        {
            postingDate = "2026-11-20", reference = "CHQ 0042",
            allocations = new[] { new { invoiceId = first.Id, amount = 10_000m }, new { invoiceId = second.Id, amount = 2_000m } },
        }, Ct)).Content.ReadFromJsonAsync<PaymentDto>(Ct);
        Assert.Equal(12_000m, payment!.Amount);
        Assert.Equal($"{first.Number}, {second.Number}", payment.InvoiceNumber);
        Assert.Equal(2, payment.Allocations!.Count);
        Assert.Equal(0m, (await admin.GetFromJsonAsync<InvoiceDto>(U($"/api/accounting/invoices/{first.Id}"), Ct))!.Outstanding);
        Assert.Equal(3_000m, (await admin.GetFromJsonAsync<InvoiceDto>(U($"/api/accounting/invoices/{second.Id}"), Ct))!.Outstanding);
        Assert.Equal(payment.Id, Assert.Single((await admin.GetFromJsonAsync<List<PaymentDto>>(U($"/api/accounting/invoices/{second.Id}/payments"), Ct))!).Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/accounting/payments"), new
        {
            postingDate = "2026-11-20", allocations = new[] { new { invoiceId = second.Id, amount = 100m }, new { invoiceId = foreign.Id, amount = 100m } },
        }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/accounting/payments"),
            new { invoiceId = foreign.Id, amount = 100m, postingDate = "2026-11-20", bankAccountCode = "6200" }, Ct)).StatusCode);
        Assert.Equal(3_000m, (await admin.GetFromJsonAsync<InvoiceDto>(U($"/api/accounting/invoices/{foreign.Id}"), Ct))!.Outstanding); // nothing applied

        var ageing = await admin.GetFromJsonAsync<AgeingReportDto>(U($"/api/accounting/reports/ageing?companyId={id}&kind=Sales&asOf=2026-11-30"), Ct);
        Assert.Equal(3_000m, ageing!.Rows.Single(r => r.PartyName == "Merkato Traders").Total);

        Assert.Equal("Cancelled", (await (await admin.PostAsync(U($"/api/accounting/payments/{payment.Id}/cancel"), null, Ct)).Content.ReadFromJsonAsync<PaymentDto>(Ct))!.Status);
        Assert.Equal(10_000m, (await admin.GetFromJsonAsync<InvoiceDto>(U($"/api/accounting/invoices/{first.Id}"), Ct))!.Outstanding);
        Assert.Equal(5_000m, (await admin.GetFromJsonAsync<InvoiceDto>(U($"/api/accounting/invoices/{second.Id}"), Ct))!.Outstanding);

        // Bank charges and interest the books do not have yet.
        var bank = await (await admin.PostAsJsonAsync(U("/api/accounting/bank-accounts"),
            new { companyId = id, name = "Main", bankName = "Dashen Bank", accountNumber = "5001" }, Ct)).Content.ReadFromJsonAsync<BankAccountDto>(Ct);
        await admin.PostAsJsonAsync(U($"/api/accounting/bank-accounts/{bank!.Id}/statement"), new
        {
            lines = new object[]
            {
                new { date = "2026-11-28", description = "SMS banking fee", amount = -75m },
                new { date = "2026-11-30", description = "Interest", amount = 40m },
            },
        }, Ct);
        var recUrl = U($"/api/accounting/bank-accounts/{bank.Id}/reconciliation?from=2026-11-01&to=2026-11-30");
        var rec = await admin.GetFromJsonAsync<ReconciliationDto>(recUrl, Ct);
        var fee = rec!.StatementLines.Single(l => l.Amount == -75m);
        var interest = rec.StatementLines.Single(l => l.Amount == 40m);

        var feeJournal = await (await admin.PostAsJsonAsync(U($"/api/accounting/bank-accounts/{bank.Id}/lines/{fee.Id}/post"), new { }, Ct)).Content.ReadFromJsonAsync<JournalEntryDto>(Ct);
        Assert.Equal("Bank", feeJournal!.Source);
        Assert.Contains(feeJournal.Lines, l => l.AccountCode == "6400" && l.Debit == 75m);
        Assert.Contains(feeJournal.Lines, l => l.AccountCode == "1121" && l.Credit == 75m);
        var interestJournal = await (await admin.PostAsJsonAsync(U($"/api/accounting/bank-accounts/{bank.Id}/lines/{interest.Id}/post"), new { }, Ct))
            .Content.ReadFromJsonAsync<JournalEntryDto>(Ct);
        Assert.Contains(interestJournal!.Lines, l => l.AccountCode == "4300" && l.Credit == 40m);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(U($"/api/accounting/bank-accounts/{bank.Id}/lines/{fee.Id}/post"), new { }, Ct)).StatusCode);

        rec = await admin.GetFromJsonAsync<ReconciliationDto>(recUrl, Ct);
        Assert.All(rec!.StatementLines, l => Assert.True(l.Matched));
        Assert.Equal(-35m, rec.BookBalance);

        // Cancelling the fee journal unmatches its line; it cannot be booked from the statement a second time.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(U($"/api/accounting/journals/{feeJournal.Id}/cancel"), null, Ct)).StatusCode);
        rec = await admin.GetFromJsonAsync<ReconciliationDto>(recUrl, Ct);
        Assert.False(rec!.StatementLines.Single(l => l.Id == fee.Id).Matched);
        Assert.Equal(40m, rec.BookBalance);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(U($"/api/accounting/bank-accounts/{bank.Id}/lines/{fee.Id}/post"), new { }, Ct)).StatusCode);
    }
}
