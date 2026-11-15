using System.Net;
using System.Net.Http.Json;
using Gebeta.Accounting.Contracts;
using Gebeta.Core.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>Bank accounts, statement import and reconciliation against the ledger.</summary>
public class BankingFlowTests(GebetaApiFactory factory) : IClassFixture<GebetaApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task A_statement_imports_once_matches_automatically_and_by_hand_and_reconciles_to_the_bank_balance()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Bank Rec PLC", abbreviation = "BRC", legalForm = "PrivateLimitedCompany", tin = "0800000071" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var id = company!.Id;

        var main = await (await admin.PostAsJsonAsync(U("/api/accounting/bank-accounts"),
            new { companyId = id, name = "Main", bankName = "Commercial Bank of Ethiopia", accountNumber = "1000123456789" }, Ct)).Content.ReadFromJsonAsync<BankAccountDto>(Ct);
        Assert.Equal("1121", main!.LedgerAccountCode); // a new ledger account under 1100
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(U("/api/accounting/bank-accounts"),
            new { companyId = id, name = "Payroll", bankName = "Awash Bank", accountNumber = "013200000001", ledgerAccountCode = "1120" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(U("/api/accounting/bank-accounts"),
            new { companyId = id, name = "Again", bankName = "Dashen Bank", accountNumber = "5", ledgerAccountCode = "1120" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/accounting/bank-accounts"),
            new { companyId = id, name = "Wrong", bankName = "Dashen Bank", accountNumber = "6", ledgerAccountCode = "6200" }, Ct)).StatusCode);
        Assert.Equal(2, (await admin.GetFromJsonAsync<List<BankAccountDto>>(U($"/api/accounting/bank-accounts?companyId={id}"), Ct))!.Count);

        async Task<JournalEntryDto> Journal(string date, string debit, string credit, decimal amount)
        {
            var j = await (await admin.PostAsJsonAsync(U("/api/accounting/journals"), new
            {
                companyId = id, postingDate = date, remark = $"{debit}/{credit}",
                lines = new object[] { new { accountCode = debit, debit = amount, credit = 0 }, new { accountCode = credit, debit = 0, credit = amount } },
            }, Ct)).Content.ReadFromJsonAsync<JournalEntryDto>(Ct);
            return (await (await admin.PostAsync(U($"/api/accounting/journals/{j!.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<JournalEntryDto>(Ct))!;
        }

        await Journal("2026-11-10", "1121", "3100", 100_000m); // capital paid in
        await Journal("2026-11-12", "6200", "1121", 8_000m);   // rent cheque
        await Journal("2026-11-14", "2100", "1121", 2_500m);   // two equal supplier payments
        await Journal("2026-11-15", "2100", "1121", 2_500m);
        var wrong = await Journal("2026-11-13", "6300", "1121", 1_000m); // entered by mistake and cancelled
        await admin.PostAsync(U($"/api/accounting/journals/{wrong.Id}/cancel"), null, Ct);

        object[] statement =
        [
            new { date = "2026-11-10", description = "Cash deposit", reference = "DEP1", amount = 100_000m, balance = 100_000m },
            new { date = "2026-11-13", description = "Cheque 001", reference = "CHQ001", amount = -8_000m, balance = 92_000m },
            new { date = "2026-11-14", description = "Transfer", reference = "TRF1", amount = -2_500m, balance = 89_500m },
            new { date = "2026-11-15", description = "Transfer", reference = "TRF2", amount = -2_500m, balance = 87_000m },
            new { date = "2026-11-16", description = "Service charge", reference = (string?)null, amount = -150m, balance = 86_850m },
        ];
        var imported = await (await admin.PostAsJsonAsync(U($"/api/accounting/bank-accounts/{main.Id}/statement"), new { lines = statement }, Ct))
            .Content.ReadFromJsonAsync<ImportResultDto>(Ct);
        Assert.Equal(new ImportResultDto(5, 0), imported);
        Assert.Equal(new ImportResultDto(0, 5), await (await admin.PostAsJsonAsync(U($"/api/accounting/bank-accounts/{main.Id}/statement"), new { lines = statement }, Ct))
            .Content.ReadFromJsonAsync<ImportResultDto>(Ct));

        var recUrl = U($"/api/accounting/bank-accounts/{main.Id}/reconciliation?from=2026-11-01&to=2026-11-30");
        var rec = await admin.GetFromJsonAsync<ReconciliationDto>(recUrl, Ct);
        Assert.Equal(4, rec!.BookEntries.Count);               // the cancelled journal and its reversal cancel out
        Assert.Equal(2, rec.Suggestions.Count);                // deposit and cheque; the twin 2,500s are ambiguous
        Assert.Equal(87_000m, rec.BookBalance);

        Assert.Equal(2, await (await admin.PostAsync(U($"/api/accounting/bank-accounts/{main.Id}/auto-match?from=2026-11-01&to=2026-11-30"), null, Ct))
            .Content.ReadFromJsonAsync<int>(Ct));

        StatementLineDto LineOn(ReconciliationDto r, string date) => r.StatementLines.Single(l => l.Date == DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        BookEntryDto EntryOn(ReconciliationDto r, string date) => r.BookEntries.Single(e => e.Date == DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
        rec = await admin.GetFromJsonAsync<ReconciliationDto>(recUrl, Ct);

        // A wrong amount is refused; the twins are matched by hand.
        var charge = LineOn(rec!, "2026-11-16");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U($"/api/accounting/bank-accounts/{main.Id}/match"),
            new { statementLineId = charge.Id, glEntryIds = new[] { EntryOn(rec!, "2026-11-14").GlEntryId } }, Ct)).StatusCode);
        foreach (var day in new[] { "2026-11-14", "2026-11-15" })
        {
            var response = await admin.PostAsJsonAsync(U($"/api/accounting/bank-accounts/{main.Id}/match"),
                new { statementLineId = LineOn(rec!, day).Id, glEntryIds = new[] { EntryOn(rec!, day).GlEntryId } }, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(U($"/api/accounting/bank-accounts/{main.Id}/match"),
            new { statementLineId = charge.Id, glEntryIds = new[] { EntryOn(rec!, "2026-11-14").GlEntryId } }, Ct)).StatusCode);

        rec = await admin.GetFromJsonAsync<ReconciliationDto>(recUrl, Ct);
        Assert.Equal(-150m, rec!.UnmatchedStatement);  // the bank charged a fee not yet in the books
        Assert.Equal(0m, rec.UnmatchedBook);
        Assert.Equal(86_850m, rec.ExpectedStatementBalance);
        Assert.Equal(86_850m, rec.StatementBalance);
        Assert.Equal(0m, rec.Difference);
        Assert.Equal(["JV-2019-00001"], LineOn(rec, "2026-11-10").MatchedVouchers);

        // Book the fee; it matches on its own.
        await Journal("2026-11-16", "6900", "1121", 150m);
        Assert.Equal(1, await (await admin.PostAsync(U($"/api/accounting/bank-accounts/{main.Id}/auto-match?from=2026-11-01&to=2026-11-30"), null, Ct))
            .Content.ReadFromJsonAsync<int>(Ct));
        rec = await admin.GetFromJsonAsync<ReconciliationDto>(recUrl, Ct);
        Assert.All(rec!.StatementLines, l => Assert.True(l.Matched));
        Assert.Equal(86_850m, rec.BookBalance);

        // Unmatching changes nothing in the ledger.
        Assert.Equal(1, await (await admin.PostAsync(U($"/api/accounting/bank-accounts/{main.Id}/unmatch/{charge.Id}"), null, Ct)).Content.ReadFromJsonAsync<int>(Ct));
        rec = await admin.GetFromJsonAsync<ReconciliationDto>(recUrl, Ct);
        Assert.False(LineOn(rec!, "2026-11-16").Matched);
        Assert.Equal(86_850m, rec!.BookBalance);
    }
}
