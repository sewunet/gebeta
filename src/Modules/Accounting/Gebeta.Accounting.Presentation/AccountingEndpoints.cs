using Gebeta.Accounting.Application;
using Gebeta.Accounting.Contracts;
using Gebeta.Application;
using Gebeta.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Gebeta.Accounting.Presentation;

public static class AccountingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var acc = endpoints.MapGroup("/api/accounting").WithTags("Accounting");

        acc.MapGet("/accounts", async (Guid companyId, IQueryHandler<ListAccountsQuery, IReadOnlyList<AccountDto>> h, CancellationToken ct) =>
                (await h.Handle(new ListAccountsQuery(companyId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListAccounts").RequirePermission(AccountingPermissions.AccountsRead);

        acc.MapPost("/companies/{companyId:guid}/chart", async (Guid companyId, ICommandHandler<SetUpChartCommand, IReadOnlyList<AccountDto>> h, CancellationToken ct) =>
                (await h.Handle(new SetUpChartCommand(companyId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("SetUpChart").RequirePermission(AccountingPermissions.AccountsManage);

        acc.MapGet("/journals", async (Guid companyId, DateOnly from, DateOnly to, IQueryHandler<ListJournalsQuery, IReadOnlyList<JournalEntryDto>> h, CancellationToken ct) =>
                (await h.Handle(new ListJournalsQuery(companyId, from, to), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListJournals").RequirePermission(AccountingPermissions.JournalsRead);

        acc.MapGet("/journals/{id:guid}", async (Guid id, IQueryHandler<GetJournalQuery, JournalEntryDto> h, CancellationToken ct) =>
                (await h.Handle(new GetJournalQuery(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GetJournal").RequirePermission(AccountingPermissions.JournalsRead);

        acc.MapPost("/journals", async (CreateJournalCommand command, ICommandHandler<CreateJournalCommand, JournalEntryDto> h, CancellationToken ct) =>
            {
                var result = await h.Handle(command, ct).ConfigureAwait(false);
                return result.IsSuccess ? Results.Created($"/api/accounting/journals/{result.Value.Id}", result.Value) : result.ToHttp();
            })
            .WithName("CreateJournal").RequirePermission(AccountingPermissions.JournalsCreate);

        acc.MapPost("/journals/{id:guid}/submit", async (Guid id, ICommandHandler<SubmitJournalCommand, JournalEntryDto> h, CancellationToken ct) =>
                (await h.Handle(new SubmitJournalCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("SubmitJournal").RequirePermission(AccountingPermissions.JournalsSubmit);

        acc.MapPost("/journals/{id:guid}/cancel", async (Guid id, ICommandHandler<CancelJournalCommand, JournalEntryDto> h, CancellationToken ct) =>
                (await h.Handle(new CancelJournalCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("CancelJournal").RequirePermission(AccountingPermissions.JournalsCancel);

        acc.MapGet("/invoices", async (Guid companyId, Gebeta.Accounting.Domain.Invoicing.InvoiceKind? kind, DateOnly from, DateOnly to,
                IQueryHandler<ListInvoicesQuery, IReadOnlyList<InvoiceDto>> h, CancellationToken ct) =>
                (await h.Handle(new ListInvoicesQuery(companyId, kind, from, to), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListInvoices").RequirePermission(InvoicingPermissions.InvoicesRead);
        acc.MapGet("/invoices/{id:guid}", async (Guid id, IQueryHandler<GetInvoiceQuery, InvoiceDto> h, CancellationToken ct) =>
                (await h.Handle(new GetInvoiceQuery(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GetInvoice").RequirePermission(InvoicingPermissions.InvoicesRead);
        acc.MapPost("/invoices", async (CreateInvoiceCommand command, ICommandHandler<CreateInvoiceCommand, InvoiceDto> h, CancellationToken ct) =>
            {
                var result = await h.Handle(command, ct).ConfigureAwait(false);
                return result.IsSuccess ? Results.Created($"/api/accounting/invoices/{result.Value.Id}", result.Value) : result.ToHttp();
            })
            .WithName("CreateInvoice").RequirePermission(InvoicingPermissions.InvoicesCreate);
        acc.MapPost("/invoices/{id:guid}/submit", async (Guid id, ICommandHandler<SubmitInvoiceCommand, InvoiceDto> h, CancellationToken ct) =>
                (await h.Handle(new SubmitInvoiceCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("SubmitInvoice").RequirePermission(InvoicingPermissions.InvoicesSubmit);
        acc.MapPost("/invoices/{id:guid}/cancel", async (Guid id, ICommandHandler<CancelInvoiceCommand, InvoiceDto> h, CancellationToken ct) =>
                (await h.Handle(new CancelInvoiceCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("CancelInvoice").RequirePermission(InvoicingPermissions.InvoicesSubmit);
        acc.MapPost("/payments", async (CreatePaymentCommand command, ICommandHandler<CreatePaymentCommand, PaymentDto> h, CancellationToken ct) =>
                (await h.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("CreatePayment").RequirePermission(InvoicingPermissions.PaymentsCreate);
        acc.MapGet("/reports/vat", async (Guid companyId, DateOnly from, DateOnly to, IQueryHandler<VatReportQuery, VatReportDto> h, CancellationToken ct) =>
                (await h.Handle(new VatReportQuery(companyId, from, to), ct).ConfigureAwait(false)).ToHttp())
            .WithName("VatReport").RequirePermission(AccountingPermissions.ReportsRead);
        acc.MapGet("/reports/withholding", async (Guid companyId, DateOnly from, DateOnly to, IQueryHandler<WithholdingReportQuery, WithholdingReportDto> h, CancellationToken ct) =>
                (await h.Handle(new WithholdingReportQuery(companyId, from, to), ct).ConfigureAwait(false)).ToHttp())
            .WithName("WithholdingReport").RequirePermission(AccountingPermissions.ReportsRead);

        acc.MapGet("/periods", async (Guid companyId, IQueryHandler<ListPeriodClosingsQuery, PeriodClosingsDto> h, CancellationToken ct) =>
                (await h.Handle(new ListPeriodClosingsQuery(companyId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListPeriodClosings").RequirePermission(AccountingPermissions.ReportsRead);
        acc.MapPost("/periods/close", async (ClosePeriodCommand command, ICommandHandler<ClosePeriodCommand, PeriodClosingDto> h, CancellationToken ct) =>
                (await h.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("ClosePeriod").RequirePermission(AccountingPermissions.PeriodsClose);
        acc.MapPost("/periods/reopen", async (ReopenPeriodCommand command, ICommandHandler<ReopenPeriodCommand, PeriodClosingDto> h, CancellationToken ct) =>
                (await h.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("ReopenPeriod").RequirePermission(AccountingPermissions.PeriodsClose);
        acc.MapPost("/opening-balances", async (PostOpeningBalancesCommand command, ICommandHandler<PostOpeningBalancesCommand, JournalEntryDto> h, CancellationToken ct) =>
                (await h.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("PostOpeningBalances").RequirePermission(AccountingPermissions.PeriodsClose);
        acc.MapPost("/years/close", async (CloseYearCommand command, ICommandHandler<CloseYearCommand, JournalEntryDto> h, CancellationToken ct) =>
                (await h.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("CloseYear").RequirePermission(AccountingPermissions.PeriodsClose);
        acc.MapGet("/reports/profit-and-loss", async (Guid companyId, DateOnly from, DateOnly to, IQueryHandler<ProfitAndLossQuery, ProfitAndLossDto> h, CancellationToken ct) =>
                (await h.Handle(new ProfitAndLossQuery(companyId, from, to), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ProfitAndLoss").RequirePermission(AccountingPermissions.ReportsRead);
        acc.MapGet("/reports/balance-sheet", async (Guid companyId, DateOnly asOf, IQueryHandler<BalanceSheetQuery, BalanceSheetDto> h, CancellationToken ct) =>
                (await h.Handle(new BalanceSheetQuery(companyId, asOf), ct).ConfigureAwait(false)).ToHttp())
            .WithName("BalanceSheet").RequirePermission(AccountingPermissions.ReportsRead);
        acc.MapGet("/reports/general-ledger", async (Guid companyId, string account, DateOnly from, DateOnly to, IQueryHandler<GeneralLedgerQuery, GeneralLedgerDto> h,
                CancellationToken ct) =>
                (await h.Handle(new GeneralLedgerQuery(companyId, account, from, to), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GeneralLedger").RequirePermission(AccountingPermissions.ReportsRead);

        acc.MapGet("/reports/trial-balance", async (Guid companyId, DateOnly asOf, IQueryHandler<TrialBalanceQuery, TrialBalanceDto> h, CancellationToken ct) =>
                (await h.Handle(new TrialBalanceQuery(companyId, asOf), ct).ConfigureAwait(false)).ToHttp())
            .WithName("TrialBalance").RequirePermission(AccountingPermissions.ReportsRead);
    }
}
