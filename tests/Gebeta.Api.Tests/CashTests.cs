using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gebeta.Accounting.Application;
using Gebeta.Accounting.Contracts;
using Gebeta.Cash.Contracts;
using Gebeta.Cash.Infrastructure.Persistence;
using Gebeta.Core.Contracts;
using Gebeta.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Api.Tests;

/// <summary>Cash Management through the API, and its journal requests posted by Accounting.</summary>
public class CashTests(GebetaApiFactory factory) : IClassFixture<GebetaApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    /// <summary>Hands Cash's pending outbox events to Accounting's consumer, as RabbitMQ would.</summary>
    private async Task<int> DeliverCashJournalsAsync(Guid companyId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var cashDb = scope.ServiceProvider.GetRequiredService<CashDbContext>();
        var pending = await cashDb.Set<OutboxMessage>().Where(m => m.RoutingKey == "cash.journal-requested.v1" && m.ProcessedAt == null).ToListAsync(Ct);
        var delivered = 0;
        foreach (var message in pending)
        {
            var request = JsonSerializer.Deserialize<JournalRequested>(message.Payload, MessagingModel.Json)!;
            if (request.CompanyId != companyId)
            {
                continue;
            }

            await using var inner = factory.Services.CreateAsyncScope();
            var unitOfWork = inner.ServiceProvider.GetRequiredService<IAccountingUnitOfWork>();
            await using var transaction = await unitOfWork.BeginTransactionAsync(Ct);
            await inner.ServiceProvider.GetRequiredService<CashJournalRequestedConsumer>().HandleAsync(request, Ct);
            await transaction.CommitAsync(Ct);
            message.ProcessedAt = DateTimeOffset.UtcNow;
            delivered++;
        }

        await cashDb.SaveChangesAsync(Ct);
        return delivered;
    }

    [Fact]
    public async Task Cashiers_receive_pay_transfer_and_get_topped_up_and_the_books_agree()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Cash Test PLC", abbreviation = "CSH", legalForm = "PrivateLimitedCompany", tin = "0900000001" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        await admin.PostAsync(U($"/api/accounting/companies/{company!.Id}/chart"), null, Ct);
        await admin.PutAsJsonAsync(U($"/api/cash/companies/{company.Id}/settings"), new { enableAutoRequest = true, minimumBalance = 1_000, targetBalance = 5_000 }, Ct);

        var sales = await (await admin.PostAsJsonAsync(U("/api/cash/cashiers"),
            new { companyId = company.Id, code = "SALES", name = "Sales Counter", type = "SalesCashier" }, Ct)).Content.ReadFromJsonAsync<CashierDto>(Ct);
        var petty = await (await admin.PostAsJsonAsync(U("/api/cash/cashiers"),
            new { companyId = company.Id, code = "PETTY", name = "Petty Cash Office", type = "PettyCashier", autoTopUp = true }, Ct)).Content.ReadFromJsonAsync<CashierDto>(Ct);

        var receipt = await admin.PostAsJsonAsync(U("/api/cash/transactions"),
            new { cashierId = sales!.Id, kind = "Receipt", amount = 10_000, counterAccountCode = "4100", description = "Cash sales", date = "2026-11-16" }, Ct);
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        Assert.Equal("CR-2019-00001", (await receipt.Content.ReadFromJsonAsync<CashMovementDto>(Ct))!.Number);

        var transfer = await (await admin.PostAsJsonAsync(U("/api/cash/transfers"),
            new { fromCashierId = sales.Id, toCashierId = petty!.Id, amount = 3_000, date = "2026-11-16" }, Ct)).Content.ReadFromJsonAsync<CashMovementDto>(Ct);
        Assert.Equal(7_000m, transfer!.BalanceAfter);

        // Paying 2,500 leaves 500, below the 1,000 minimum: an automatic request for 4,500 appears.
        var payment = await (await admin.PostAsJsonAsync(U("/api/cash/transactions"),
            new { cashierId = petty.Id, kind = "Payment", amount = 2_500, counterAccountCode = "6900", description = "Office supplies", date = "2026-11-16" }, Ct))
            .Content.ReadFromJsonAsync<CashMovementDto>(Ct);
        Assert.Equal(500m, payment!.BalanceAfter);
        Assert.NotNull(payment.TopUpRequest);
        Assert.True(payment.TopUpRequest!.CreatedBySystem);
        Assert.Equal(4_500m, payment.TopUpRequest.RequestedAmount);
        Assert.Equal("Pending", payment.TopUpRequest.Status);

        var overdraw = await admin.PostAsJsonAsync(U("/api/cash/transactions"),
            new { cashierId = petty.Id, kind = "Payment", amount = 600, counterAccountCode = "6900", description = "Too much", date = "2026-11-16" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, overdraw.StatusCode);
        Assert.Contains("overdraw", await overdraw.Content.ReadAsStringAsync(Ct), StringComparison.OrdinalIgnoreCase);

        var approved = await (await admin.PostAsJsonAsync(U($"/api/cash/petty-cash-requests/{payment.TopUpRequest.Id}/approve"), new { fundingFrom = "SourceAccount" }, Ct))
            .Content.ReadFromJsonAsync<PettyCashRequestDto>(Ct);
        Assert.Equal("ApprovedAndPaid", approved!.Status);
        Assert.Equal("1120", approved.SourceAccountCode);

        // A request funded by another cashier cannot overdraw that cashier.
        var manual = await (await admin.PostAsJsonAsync(U("/api/cash/petty-cash-requests"), new { cashierId = sales.Id, amount = 6_000, remarks = "Change float" }, Ct))
            .Content.ReadFromJsonAsync<PettyCashRequestDto>(Ct);
        var refused = await admin.PostAsJsonAsync(U($"/api/cash/petty-cash-requests/{manual!.Id}/approve"), new { fundingFrom = "Cashier", fundingCashierId = petty.Id }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var rejected = await (await admin.PostAsJsonAsync(U($"/api/cash/petty-cash-requests/{manual.Id}/reject"), new { reason = "Use the bank" }, Ct))
            .Content.ReadFromJsonAsync<PettyCashRequestDto>(Ct);
        Assert.Equal("Rejected", rejected!.Status);

        var cashiers = await admin.GetFromJsonAsync<List<CashierDto>>(U($"/api/cash/cashiers?companyId={company.Id}"), Ct);
        Assert.Equal(7_000m, cashiers!.Single(c => c.Code == "SALES").Balance);
        Assert.Equal(5_000m, cashiers!.Single(c => c.Code == "PETTY").Balance);
        var ledger = await admin.GetFromJsonAsync<List<CashLedgerEntryDto>>(U($"/api/cash/cashiers/{petty.Id}/ledger"), Ct);
        Assert.Equal(["TransferIn", "Payment", "TopUp"], ledger!.Select(e => e.Kind));

        // Accounting posts the four journal requests; the GL cash account equals the cashiers' balances.
        Assert.Equal(4, await DeliverCashJournalsAsync(company.Id));
        var tb = await admin.GetFromJsonAsync<TrialBalanceDto>(U($"/api/accounting/reports/trial-balance?companyId={company.Id}&asOf=2026-11-30"), Ct);
        Assert.Equal(12_000m, tb!.Rows.Single(r => r.Code == "1110").Balance);   // 7,000 + 5,000
        Assert.Equal(10_000m, tb.Rows.Single(r => r.Code == "4100").Balance);
        Assert.Equal(2_500m, tb.Rows.Single(r => r.Code == "6900").Balance);
        Assert.Equal(-4_500m, tb.Rows.Single(r => r.Code == "1120").Balance);
        Assert.Equal(tb.TotalDebit, tb.TotalCredit);

        var journals = await admin.GetFromJsonAsync<List<JournalEntryDto>>(U($"/api/accounting/journals?companyId={company.Id}&from=2026-11-01&to=2026-11-30"), Ct);
        var transferJournal = journals!.Single(j => j.SourceNumber!.StartsWith("CT-", StringComparison.Ordinal));
        Assert.Equal(2, transferJournal.Lines.Count); // one line per cashier, not merged into nothing
        Assert.All(journals!, j => Assert.Equal("Cash", j.Source));
    }
}
