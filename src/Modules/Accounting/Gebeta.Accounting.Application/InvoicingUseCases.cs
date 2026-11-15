using Gebeta.Accounting.Contracts;
using Gebeta.Accounting.Domain.Invoicing;
using Gebeta.Accounting.Domain.Ledger;
using Gebeta.Application;
using Gebeta.SharedKernel;

namespace Gebeta.Accounting.Application;

/// <summary>Accounting settings (configuration section <c>Accounting</c>): Ethiopian VAT and withholding.</summary>
public sealed class AccountingOptions
{
    public const string Section = "Accounting";

    public decimal VatRatePercent { get; set; } = TaxRules.EthiopianDefault.VatRatePercent;
    public decimal WithholdingRatePercent { get; set; } = TaxRules.EthiopianDefault.WithholdingRatePercent;
    public decimal WithholdingGoodsThreshold { get; set; } = TaxRules.EthiopianDefault.WithholdingGoodsThreshold;
    public decimal WithholdingServicesThreshold { get; set; } = TaxRules.EthiopianDefault.WithholdingServicesThreshold;

    /// <summary>Whether this company withholds tax from its suppliers (designated withholding agent).</summary>
    public bool IsWithholdingAgent { get; set; } = true;

    public TaxRules Rules => new(VatRatePercent, WithholdingRatePercent, WithholdingGoodsThreshold, WithholdingServicesThreshold);
}

public sealed record InvoiceLineInput(string Description, decimal Quantity, decimal Rate, string AccountCode, bool IsService, bool VatApplicable = true);

/// <param name="CustomerWithholds">Sales only: the customer is a withholding agent and deducts withholding tax.</param>
public sealed record CreateInvoiceCommand(InvoiceKind Kind, Guid CompanyId, Guid PartyId, DateOnly PostingDate, DateOnly? DueDate,
    IReadOnlyList<InvoiceLineInput> Lines, string? SupplierInvoiceNumber, bool CustomerWithholds = false) : ICommand<InvoiceDto>;

public sealed record SubmitInvoiceCommand(Guid InvoiceId) : ICommand<InvoiceDto>;

public sealed record CancelInvoiceCommand(Guid InvoiceId) : ICommand<InvoiceDto>;

public sealed record GetInvoiceQuery(Guid InvoiceId) : IQuery<InvoiceDto>;

public sealed record ListInvoicesQuery(Guid CompanyId, InvoiceKind? Kind, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<InvoiceDto>>;

public sealed record CreatePaymentCommand(Guid InvoiceId, decimal Amount, DateOnly PostingDate, string? BankAccountCode, string? Reference) : ICommand<PaymentDto>;

public sealed record VatReportQuery(Guid CompanyId, DateOnly From, DateOnly To) : IQuery<VatReportDto>;

public sealed record WithholdingReportQuery(Guid CompanyId, DateOnly From, DateOnly To) : IQuery<WithholdingReportDto>;

public sealed class InvoicingHandler(
    IInvoiceRepository invoices,
    IAccountRepository accounts,
    IJournalRepository journals,
    ChartSetup chart,
    IPartyDirectory parties,
    ICompanyDirectory companies,
    ICompanyAccess access,
    ICurrentUser currentUser,
    IAccountingNumbers numbers,
    IAccountingUnitOfWork unitOfWork,
    AccountingOptions options,
    TimeProvider clock)
    : ICommandHandler<CreateInvoiceCommand, InvoiceDto>,
      ICommandHandler<SubmitInvoiceCommand, InvoiceDto>,
      ICommandHandler<CancelInvoiceCommand, InvoiceDto>,
      IQueryHandler<GetInvoiceQuery, InvoiceDto>,
      IQueryHandler<ListInvoicesQuery, IReadOnlyList<InvoiceDto>>,
      ICommandHandler<CreatePaymentCommand, PaymentDto>,
      IQueryHandler<VatReportQuery, VatReportDto>,
      IQueryHandler<WithholdingReportQuery, WithholdingReportDto>
{
    public async Task<Result<InvoiceDto>> Handle(CreateInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false)
            || await companies.GetAsync(command.CompanyId, cancellationToken).ConfigureAwait(false) is not { } company)
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        var party = await parties.GetAsync(command.PartyId, cancellationToken).ConfigureAwait(false);
        if (party is null || party.CompanyId != command.CompanyId)
        {
            return Error.NotFound(command.Kind == InvoiceKind.Sales ? "Customer" : "Supplier", command.PartyId);
        }

        if (await chart.EnsureAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var byCode = (await accounts.ListAsync(command.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Code);
        var bad = (command.Lines ?? []).FirstOrDefault(l => !byCode.TryGetValue(l.AccountCode, out var a) || a.IsGroup);
        if (bad is not null)
        {
            return Error.Validation($"Account {bad.AccountCode} is not a postable account of this company.");
        }

        Invoice invoice;
        try
        {
            var vatCharged = command.Kind == InvoiceKind.Sales ? company.IsVatRegistered : party.IsVatRegistered;
            var withholding = command.Kind == InvoiceKind.Purchase ? options.IsWithholdingAgent : command.CustomerWithholds;
            invoice = Invoice.Create(command.Kind, command.CompanyId, party.Id, party.Name, party.Tin, command.PostingDate, command.DueDate ?? command.PostingDate.AddDays(30),
                (command.Lines ?? []).Select(l => new InvoiceLine(l.Description, l.Quantity, l.Rate, l.AccountCode, l.IsService, l.VatApplicable)).ToList(),
                options.Rules, vatCharged, withholding, command.SupplierInvoiceNumber);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        invoice.AssignName(await numbers.NextAsync(command.CompanyId, invoice.DocumentType, command.Kind == InvoiceKind.Sales ? "SINV-.FY.-.#####" : "PINV-.FY.-.#####",
            command.PostingDate, cancellationToken).ConfigureAwait(false));
        invoice.CreatedAt = clock.GetUtcNow();
        invoice.CreatedBy = currentUser.Username;
        invoices.Add(invoice);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(invoice);
    }

    public async Task<Result<InvoiceDto>> Handle(SubmitInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var invoice = await AccessibleAsync(command.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return Error.NotFound("Invoice", command.InvoiceId);
        }

        try
        {
            invoice.Submit();
            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await PostAsync(invoice.CompanyId, invoice.PostingDate, $"{invoice.DocumentType} {invoice.Name} · {invoice.PartyName}", invoice.JournalLines(),
                invoice.Kind == InvoiceKind.Sales ? JournalSource.SalesInvoice : JournalSource.PurchaseInvoice, invoice.Id, invoice.Name!, cancellationToken).ConfigureAwait(false);
            invoice.ModifiedAt = clock.GetUtcNow();
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        return ToDto(invoice);
    }

    public async Task<Result<InvoiceDto>> Handle(CancelInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var invoice = await AccessibleAsync(command.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return Error.NotFound("Invoice", command.InvoiceId);
        }

        try
        {
            invoice.Cancel();
            if (await journals.FindBySourceAsync(invoice.Id, cancellationToken).ConfigureAwait(false) is { DocStatus: DocStatus.Submitted } journal)
            {
                journals.Add(journal.Reverse());
            }
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        invoice.ModifiedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(invoice);
    }

    public async Task<Result<InvoiceDto>> Handle(GetInvoiceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var invoice = await AccessibleAsync(query.InvoiceId, cancellationToken).ConfigureAwait(false);
        return invoice is null ? Error.NotFound("Invoice", query.InvoiceId) : ToDto(invoice);
    }

    public async Task<Result<IReadOnlyList<InvoiceDto>>> Handle(ListInvoicesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        return Result.Success<IReadOnlyList<InvoiceDto>>(
            (await invoices.ListAsync(query.CompanyId, query.Kind, query.From, query.To, cancellationToken).ConfigureAwait(false)).Select(ToDto).ToList());
    }

    public async Task<Result<PaymentDto>> Handle(CreatePaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var invoice = await AccessibleAsync(command.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return Error.NotFound("Invoice", command.InvoiceId);
        }

        try
        {
            invoice.ApplyPayment(command.Amount);
            var payment = Payment.Create(invoice, command.Amount, command.PostingDate, command.BankAccountCode ?? EthiopianChartOfAccounts.Bank, command.Reference);
            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            payment.AssignName(await numbers.NextAsync(invoice.CompanyId, payment.DocumentType, "PAY-.FY.-.#####", command.PostingDate, cancellationToken).ConfigureAwait(false));
            payment.Submit();
            payment.CreatedAt = clock.GetUtcNow();
            payment.CreatedBy = currentUser.Username;
            invoices.Add(payment);
            await PostAsync(invoice.CompanyId, command.PostingDate, $"Payment {payment.Name} · {invoice.PartyName} · {invoice.Name}", payment.JournalLines(),
                JournalSource.Payment, payment.Id, payment.Name!, cancellationToken).ConfigureAwait(false);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new PaymentDto(payment.Id, payment.Name, payment.Kind.ToString(), payment.PartyId, payment.PartyName, payment.InvoiceId, payment.InvoiceNumber,
                payment.Amount, payment.BankAccountCode, payment.PostingDate, payment.Reference);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }
    }

    public async Task<Result<VatReportDto>> Handle(VatReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var submitted = (await invoices.ListAsync(query.CompanyId, null, query.From, query.To, cancellationToken).ConfigureAwait(false))
            .Where(i => i.DocStatus == DocStatus.Submitted && i.VatAmount != 0m).ToList();
        var rows = submitted.Select(i => new VatReportRow(i.Kind.ToString(), i.Name, i.PostingDate, i.PartyName, i.PartyTin, i.NetTotal, i.VatAmount)).ToList();
        var output = submitted.Where(i => i.Kind == InvoiceKind.Sales).Sum(i => i.VatAmount);
        var input = submitted.Where(i => i.Kind == InvoiceKind.Purchase).Sum(i => i.VatAmount);
        return new VatReportDto(query.From, query.To, output, input, output - input, rows);
    }

    public async Task<Result<WithholdingReportDto>> Handle(WithholdingReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var withheld = (await invoices.ListAsync(query.CompanyId, null, query.From, query.To, cancellationToken).ConfigureAwait(false))
            .Where(i => i.DocStatus == DocStatus.Submitted && i.WithholdingAmount != 0m).ToList();
        WithholdingRow Row(Invoice i) => new(i.Name, i.PostingDate, i.PartyName, i.PartyTin, i.NetTotal, i.WithholdingAmount);
        var fromSuppliers = withheld.Where(i => i.Kind == InvoiceKind.Purchase).Select(Row).ToList();
        var byCustomers = withheld.Where(i => i.Kind == InvoiceKind.Sales).Select(Row).ToList();
        return new WithholdingReportDto(query.From, query.To, fromSuppliers.Sum(r => r.Withheld), byCustomers.Sum(r => r.Withheld), fromSuppliers, byCustomers);
    }

    private async Task PostAsync(Guid companyId, DateOnly date, string remark, IReadOnlyList<(string AccountCode, decimal Debit, decimal Credit, string Description)> lines,
        JournalSource source, Guid sourceId, string sourceNumber, CancellationToken cancellationToken)
    {
        var byCode = (await accounts.ListAsync(companyId, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Code);
        var journal = JournalEntry.Create(companyId, date, remark,
            lines.Select(l => new JournalLine(byCode[l.AccountCode].Id, l.Debit, l.Credit, l.Description)).ToList(), source, sourceId, sourceNumber);
        journal.AssignName(await numbers.NextAsync(companyId, journal.DocumentType, JournalHandler.DefaultPattern, date, cancellationToken).ConfigureAwait(false));
        journal.CreatedAt = clock.GetUtcNow();
        journal.CreatedBy = currentUser.Username;
        journals.Add(journal);
        journals.Add(journal.Post());
    }

    private async Task<Invoice?> AccessibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await invoices.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return invoice is not null && await access.CanAccessAsync(invoice.CompanyId, cancellationToken).ConfigureAwait(false) ? invoice : null;
    }

    private static InvoiceDto ToDto(Invoice i) => new(i.Id, i.Name, i.Kind.ToString(), i.CompanyId, i.PartyId, i.PartyName, i.PartyTin, i.SupplierInvoiceNumber,
        i.PostingDate, i.DueDate, i.DocStatus.ToString(),
        i.Lines.Select(l => new InvoiceLineDto(l.Description, l.Quantity, l.Rate, l.Amount, l.AccountCode, l.IsService, l.VatApplicable)).ToList(),
        i.NetTotal, i.VatAmount, i.GrandTotal, i.WithholdingAmount, i.PayableAmount, i.Outstanding);
}
