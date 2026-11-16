using Gebeta.Accounting.Application;
using Gebeta.Accounting.Contracts;
using Gebeta.Accounting.Domain.Ledger;
using Gebeta.Accounting.Infrastructure.Persistence;
using Gebeta.Accounting.Presentation;
using Gebeta.Application;
using Gebeta.Core.Contracts;
using Gebeta.Messaging;
using Gebeta.Modularity;
using Gebeta.Payroll.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Accounting.Infrastructure;

/// <summary>
/// Accounting (docs/modules/accounting.md): chart of accounts, journal entries, append-only general ledger
/// and trial balance. Runs on Core alone; with Payroll installed, every submitted salary slip arrives over
/// RabbitMQ and becomes a payroll journal (and a cancelled slip reverses it).
/// </summary>
public sealed class AccountingModule : IErpModule
{
    public const string Id = "accounting";

    public ModuleInfo Info { get; } = new(Id, "Accounting", new Version(0, 1, 0));
    public IReadOnlyList<string> Requires { get; } = [ModuleLoader.CoreModuleId];
    public IReadOnlyList<string> Enhances { get; } = ["payroll", "hr", "cash", "stock"];

    public void Register(ModuleRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;
        var connectionString = context.Configuration.GetConnectionString("Main")
            ?? throw new ModuleLoadException("Connection string 'Main' is missing.");

        services.AddDbContext<AccountingDbContext>(options => AccountingDbContext.Configure(options, connectionString));
        services.AddScoped<IAccountingUnitOfWork>(sp => sp.GetRequiredService<AccountingDbContext>());
        services.AddScoped<IAccountingNumbers, AccountingNumbers>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IJournalRepository, JournalRepository>();
        services.AddScoped<IModuleMigrator, AccountingMigrator>();
        services.AddScoped<ChartSetup>();
        var accountingOptions = new AccountingOptions();
        context.Configuration.GetSection(AccountingOptions.Section).Bind(accountingOptions);
        services.AddSingleton(accountingOptions);
        services.AddScoped<Gebeta.Accounting.Domain.Invoicing.IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<InvoicingHandler>();
        services.AddScoped<IPeriodClosingRepository, PeriodClosingRepository>();
        services.AddScoped<PeriodsAndReportsHandler>();
        services.AddScoped<OpeningAndYearEndHandler>();
        services.AddScoped<Gebeta.Accounting.Domain.Banking.IBankRepository, BankRepository>();
        services.AddScoped<BankingHandler>();
        services.AddScoped<IQueryHandler<ListBankAccountsQuery, IReadOnlyList<BankAccountDto>>>(sp => sp.GetRequiredService<BankingHandler>());
        services.AddScoped<ICommandHandler<CreateBankAccountCommand, BankAccountDto>>(sp => sp.GetRequiredService<BankingHandler>());
        services.AddScoped<ICommandHandler<ImportStatementCommand, ImportResultDto>>(sp => sp.GetRequiredService<BankingHandler>());
        services.AddScoped<IQueryHandler<ReconciliationQuery, ReconciliationDto>>(sp => sp.GetRequiredService<BankingHandler>());
        services.AddScoped<ICommandHandler<MatchStatementLineCommand, ReconciliationDto>>(sp => sp.GetRequiredService<BankingHandler>());
        services.AddScoped<ICommandHandler<UnmatchStatementLineCommand, int>>(sp => sp.GetRequiredService<BankingHandler>());
        services.AddScoped<ICommandHandler<AutoMatchCommand, int>>(sp => sp.GetRequiredService<BankingHandler>());
        services.AddScoped<ICommandHandler<PostStatementLineCommand, JournalEntryDto>>(sp => sp.GetRequiredService<BankingHandler>());
        services.AddScoped<ICommandHandler<PostOpeningBalancesCommand, JournalEntryDto>>(sp => sp.GetRequiredService<OpeningAndYearEndHandler>());
        services.AddScoped<ICommandHandler<CloseYearCommand, JournalEntryDto>>(sp => sp.GetRequiredService<OpeningAndYearEndHandler>());
        services.AddScoped<ICommandHandler<ClosePeriodCommand, PeriodClosingDto>>(sp => sp.GetRequiredService<PeriodsAndReportsHandler>());
        services.AddScoped<ICommandHandler<ReopenPeriodCommand, PeriodClosingDto>>(sp => sp.GetRequiredService<PeriodsAndReportsHandler>());
        services.AddScoped<IQueryHandler<ListPeriodClosingsQuery, PeriodClosingsDto>>(sp => sp.GetRequiredService<PeriodsAndReportsHandler>());
        services.AddScoped<IQueryHandler<ProfitAndLossQuery, ProfitAndLossDto>>(sp => sp.GetRequiredService<PeriodsAndReportsHandler>());
        services.AddScoped<IQueryHandler<BalanceSheetQuery, BalanceSheetDto>>(sp => sp.GetRequiredService<PeriodsAndReportsHandler>());
        services.AddScoped<IQueryHandler<GeneralLedgerQuery, GeneralLedgerDto>>(sp => sp.GetRequiredService<PeriodsAndReportsHandler>());
        services.AddScoped<ICommandHandler<CreateInvoiceCommand, InvoiceDto>>(sp => sp.GetRequiredService<InvoicingHandler>());
        services.AddScoped<ICommandHandler<SubmitInvoiceCommand, InvoiceDto>>(sp => sp.GetRequiredService<InvoicingHandler>());
        services.AddScoped<ICommandHandler<CancelInvoiceCommand, InvoiceDto>>(sp => sp.GetRequiredService<InvoicingHandler>());
        services.AddScoped<IQueryHandler<GetInvoiceQuery, InvoiceDto>>(sp => sp.GetRequiredService<InvoicingHandler>());
        services.AddScoped<IQueryHandler<ListInvoicesQuery, IReadOnlyList<InvoiceDto>>>(sp => sp.GetRequiredService<InvoicingHandler>());
        services.AddScoped<ICommandHandler<CreatePaymentCommand, PaymentDto>>(sp => sp.GetRequiredService<InvoicingHandler>());
        services.AddScoped<IQueryHandler<VatReportQuery, VatReportDto>>(sp => sp.GetRequiredService<InvoicingHandler>());
        services.AddScoped<IQueryHandler<WithholdingReportQuery, WithholdingReportDto>>(sp => sp.GetRequiredService<InvoicingHandler>());
        services.AddScoped<ICommandHandler<CreateReturnCommand, InvoiceDto>>(sp => sp.GetRequiredService<InvoicingHandler>());
        services.AddScoped<ICommandHandler<CreateOpeningInvoiceCommand, InvoiceDto>>(sp => sp.GetRequiredService<InvoicingHandler>());
        services.AddScoped<IQueryHandler<ListInvoicePaymentsQuery, IReadOnlyList<PaymentDto>>>(sp => sp.GetRequiredService<InvoicingHandler>());
        services.AddScoped<ICommandHandler<CancelPaymentCommand, PaymentDto>>(sp => sp.GetRequiredService<InvoicingHandler>());
        services.AddScoped<IQueryHandler<AgeingReportQuery, AgeingReportDto>>(sp => sp.GetRequiredService<InvoicingHandler>());
        services.AddScoped<IQueryHandler<GetWithholdingReceiptQuery, WithholdingReceiptDto>>(sp => sp.GetRequiredService<InvoicingHandler>());
        services.AddOutboxDispatcher<AccountingDbContext>(Id, AccountingDbContext.SchemaName);

        // Events from other modules (received only when those modules are installed).
        services.AddIntegrationEventConsumer<CompanyCreated, CompanyCreatedConsumer, AccountingDbContext>(Id);
        services.AddIntegrationEventConsumer<SalarySlipSubmitted, SalarySlipSubmittedConsumer, AccountingDbContext>(Id);
        services.AddIntegrationEventConsumer<SalarySlipCancelled, SalarySlipCancelledConsumer, AccountingDbContext>(Id);
        services.AddIntegrationEventConsumer<Gebeta.Cash.Contracts.JournalRequested, CashJournalRequestedConsumer, AccountingDbContext>(Id);
        services.AddIntegrationEventConsumer<Gebeta.Stock.Contracts.StockJournalRequested, StockJournalRequestedConsumer, AccountingDbContext>(Id);
        services.AddIntegrationEventConsumer<Gebeta.Stock.Contracts.StockEntryCancelled, StockEntryCancelledConsumer, AccountingDbContext>(Id);

        services.AddScoped<JournalHandler>();
        services.AddScoped<IQueryHandler<ListAccountsQuery, IReadOnlyList<AccountDto>>>(sp => sp.GetRequiredService<JournalHandler>());
        services.AddScoped<ICommandHandler<SetUpChartCommand, IReadOnlyList<AccountDto>>>(sp => sp.GetRequiredService<JournalHandler>());
        services.AddScoped<ICommandHandler<CreateJournalCommand, JournalEntryDto>>(sp => sp.GetRequiredService<JournalHandler>());
        services.AddScoped<ICommandHandler<SubmitJournalCommand, JournalEntryDto>>(sp => sp.GetRequiredService<JournalHandler>());
        services.AddScoped<ICommandHandler<CancelJournalCommand, JournalEntryDto>>(sp => sp.GetRequiredService<JournalHandler>());
        services.AddScoped<IQueryHandler<GetJournalQuery, JournalEntryDto>>(sp => sp.GetRequiredService<JournalHandler>());
        services.AddScoped<IQueryHandler<ListJournalsQuery, IReadOnlyList<JournalEntryDto>>>(sp => sp.GetRequiredService<JournalHandler>());
        services.AddScoped<IQueryHandler<TrialBalanceQuery, TrialBalanceDto>>(sp => sp.GetRequiredService<JournalHandler>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => AccountingEndpoints.Map(endpoints);
}
