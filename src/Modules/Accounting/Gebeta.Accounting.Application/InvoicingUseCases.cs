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

/// <param name="ItemId">Optional Core item; the line keeps its code. For purchases of stock items the account is usually 2150 (cleared against goods received).</param>
public sealed record InvoiceLineInput(string Description, decimal Quantity, decimal Rate, string AccountCode, bool IsService, bool VatApplicable = true, Guid? ItemId = null);

/// <param name="CustomerWithholds">Sales only: the customer is a withholding agent and deducts withholding tax.</param>
public sealed record CreateInvoiceCommand(InvoiceKind Kind, Guid CompanyId, Guid PartyId, DateOnly PostingDate, DateOnly? DueDate,
    IReadOnlyList<InvoiceLineInput> Lines, string? SupplierInvoiceNumber, bool CustomerWithholds = false) : ICommand<InvoiceDto>;

public sealed record SubmitInvoiceCommand(Guid InvoiceId) : ICommand<InvoiceDto>;

public sealed record CancelInvoiceCommand(Guid InvoiceId) : ICommand<InvoiceDto>;

public sealed record GetInvoiceQuery(Guid InvoiceId) : IQuery<InvoiceDto>;

public sealed record ListInvoicesQuery(Guid CompanyId, InvoiceKind? Kind, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<InvoiceDto>>;

public sealed record PaymentAllocationInput(Guid InvoiceId, decimal Amount);

/// <summary>A payment for one invoice (<see cref="InvoiceId"/> and <see cref="Amount"/>) or several of the same party (<see cref="Allocations"/>).</summary>
public sealed record CreatePaymentCommand(Guid? InvoiceId, decimal? Amount, DateOnly PostingDate, string? BankAccountCode, string? Reference,
    IReadOnlyList<PaymentAllocationInput>? Allocations = null) : ICommand<PaymentDto>;

/// <summary>A credit note (sales) or debit note (purchase) against a submitted invoice; without lines, the whole invoice.</summary>
public sealed record CreateReturnCommand(Guid InvoiceId, DateOnly PostingDate, IReadOnlyList<InvoiceLineInput>? Lines) : ICommand<InvoiceDto>;

/// <summary>An invoice still open at cut-over, for its outstanding amount; posts nothing (the opening balances journal holds it).</summary>
public sealed record CreateOpeningInvoiceCommand(InvoiceKind Kind, Guid CompanyId, Guid PartyId, DateOnly PostingDate, DateOnly? DueDate, decimal Amount, string? Reference)
    : ICommand<InvoiceDto>;

public sealed record ListInvoicePaymentsQuery(Guid InvoiceId) : IQuery<IReadOnlyList<PaymentDto>>;

public sealed record CancelPaymentCommand(Guid PaymentId) : ICommand<PaymentDto>;

public sealed record GetWithholdingReceiptQuery(Guid InvoiceId) : IQuery<WithholdingReceiptDto>;

public sealed record AgeingReportQuery(Guid CompanyId, InvoiceKind Kind, DateOnly AsOf) : IQuery<AgeingReportDto>;

public sealed record VatReportQuery(Guid CompanyId, DateOnly From, DateOnly To) : IQuery<VatReportDto>;

public sealed record WithholdingReportQuery(Guid CompanyId, DateOnly From, DateOnly To) : IQuery<WithholdingReportDto>;

public sealed class InvoicingHandler(
    IInvoiceRepository invoices,
    IAccountRepository accounts,
    IJournalRepository journals,
    ChartSetup chart,
    IPartyDirectory parties,
    IItemDirectory items,
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
      IQueryHandler<WithholdingReportQuery, WithholdingReportDto>,
      ICommandHandler<CreateReturnCommand, InvoiceDto>,
      ICommandHandler<CreateOpeningInvoiceCommand, InvoiceDto>,
      IQueryHandler<ListInvoicePaymentsQuery, IReadOnlyList<PaymentDto>>,
      ICommandHandler<CancelPaymentCommand, PaymentDto>,
      IQueryHandler<AgeingReportQuery, AgeingReportDto>,
      IQueryHandler<GetWithholdingReceiptQuery, WithholdingReceiptDto>
{
    public const string WithholdingReceiptType = "Withholding Receipt";

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

        var itemCodes = new Dictionary<Guid, string>();
        foreach (var itemId in (command.Lines ?? []).Where(l => l.ItemId is not null).Select(l => l.ItemId!.Value).Distinct())
        {
            if (await items.GetAsync(itemId, cancellationToken).ConfigureAwait(false) is not { } info || info.CompanyId != command.CompanyId)
            {
                return Error.Validation("An item on the invoice is not one of this company's items.");
            }

            itemCodes[itemId] = info.Code;
        }

        Invoice invoice;
        try
        {
            var vatCharged = command.Kind == InvoiceKind.Sales ? company.IsVatRegistered : party.IsVatRegistered;
            var withholding = command.Kind == InvoiceKind.Purchase ? options.IsWithholdingAgent : command.CustomerWithholds;
            invoice = Invoice.Create(command.Kind, command.CompanyId, party.Id, party.Name, party.Tin, command.PostingDate, command.DueDate ?? command.PostingDate.AddDays(30),
                (command.Lines ?? []).Select(l => new InvoiceLine(l.Description, l.Quantity, l.Rate, l.AccountCode, l.IsService, l.VatApplicable, l.ItemId,
                    l.ItemId is { } item ? itemCodes[item] : null)).ToList(),
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
            if (invoice.IsReturn)
            {
                var original = await invoices.GetAsync(invoice.ReturnAgainstId!.Value, cancellationToken).ConfigureAwait(false)
                    ?? throw new DomainException("accounting.return_source", "The original invoice no longer exists.");
                original.ApplyReturn(invoice);
                original.ModifiedAt = clock.GetUtcNow();
            }

            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            if (invoice.NeedsWithholdingReceipt)
            {
                invoice.AssignWithholdingReceipt(await numbers.NextAsync(invoice.CompanyId, WithholdingReceiptType, "WHT-.FY.-.#####", invoice.PostingDate, cancellationToken)
                    .ConfigureAwait(false));
            }

            if (!invoice.IsOpening)
            {
                await PostAsync(invoice.CompanyId, invoice.PostingDate, $"{invoice.DocumentType} {invoice.Name} · {invoice.PartyName}", invoice.JournalLines(),
                    invoice.Kind == InvoiceKind.Sales ? JournalSource.SalesInvoice : JournalSource.PurchaseInvoice, invoice.Id, invoice.Name!, cancellationToken).ConfigureAwait(false);
            }

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
            if (invoice.IsReturn && await invoices.GetAsync(invoice.ReturnAgainstId!.Value, cancellationToken).ConfigureAwait(false) is { } original)
            {
                original.ReverseReturn(invoice);
                original.ModifiedAt = clock.GetUtcNow();
            }

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
        var requested = command.Allocations is { Count: > 0 }
            ? command.Allocations.ToList()
            : command.InvoiceId is { } single ? [new PaymentAllocationInput(single, command.Amount ?? 0m)] : [];
        if (requested.Count == 0)
        {
            return Error.Validation("Say which invoice or invoices the payment is for.");
        }

        var shares = new List<(Invoice, decimal)>();
        foreach (var r in requested)
        {
            if (await AccessibleAsync(r.InvoiceId, cancellationToken).ConfigureAwait(false) is not { } found)
            {
                return Error.NotFound("Invoice", r.InvoiceId);
            }

            shares.Add((found, r.Amount));
        }

        var invoice = shares[0].Item1;
        var bankCode = string.IsNullOrWhiteSpace(command.BankAccountCode) ? EthiopianChartOfAccounts.Bank : command.BankAccountCode.Trim();
        var bankAccount = (await accounts.ListAsync(invoice.CompanyId, cancellationToken).ConfigureAwait(false)).FirstOrDefault(a => a.Code == bankCode);
        if (bankAccount is null || bankAccount.IsGroup || bankAccount.RootType != RootType.Asset)
        {
            return Error.Validation($"Account {bankCode} is not a cash or bank account of this company.");
        }

        try
        {
            var payment = Payment.Create(shares, command.PostingDate, bankCode, command.Reference);
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
            return ToDto(payment);
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
        static decimal Sign(Invoice i) => i.IsReturn ? -1m : 1m;
        var rows = submitted.Select(i => new VatReportRow(i.Kind.ToString(), i.Name, i.PostingDate, i.PartyName, i.PartyTin, Sign(i) * i.NetTotal, Sign(i) * i.VatAmount)).ToList();
        var output = submitted.Where(i => i.Kind == InvoiceKind.Sales).Sum(i => Sign(i) * i.VatAmount);
        var input = submitted.Where(i => i.Kind == InvoiceKind.Purchase).Sum(i => Sign(i) * i.VatAmount);
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
        WithholdingRow Row(Invoice i) => i.IsReturn
            ? new(i.Name, i.PostingDate, i.PartyName, i.PartyTin, -i.WithholdingBase, -i.WithholdingAmount, null, i.SupplyType, i.WithholdingRatePercent)
            : new(i.Name, i.PostingDate, i.PartyName, i.PartyTin, i.WithholdingBase, i.WithholdingAmount, i.WithholdingReceiptNumber, i.SupplyType, i.WithholdingRatePercent);
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
        i.Lines.Select(l => new InvoiceLineDto(l.Description, l.Quantity, l.Rate, l.Amount, l.AccountCode, l.IsService, l.VatApplicable, l.ItemId, l.ItemCode)).ToList(),
        i.NetTotal, i.VatAmount, i.GrandTotal, i.WithholdingAmount, i.PayableAmount, i.Outstanding, i.IsReturn, i.ReturnAgainstNumber, i.IsOpening, i.WithholdingReceiptNumber);

    public async Task<Result<WithholdingReceiptDto>> Handle(GetWithholdingReceiptQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var invoice = await AccessibleAsync(query.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (invoice?.WithholdingReceiptNumber is null)
        {
            return Error.NotFound("Withholding receipt", query.InvoiceId);
        }

        var company = await companies.GetAsync(invoice.CompanyId, cancellationToken).ConfigureAwait(false);
        return new WithholdingReceiptDto(invoice.Id, invoice.WithholdingReceiptNumber, invoice.PostingDate, invoice.DocStatus.ToString(), company?.Name ?? string.Empty,
            company?.Tin ?? string.Empty, invoice.PartyName, invoice.PartyTin, invoice.Name, invoice.SupplierInvoiceNumber, invoice.SupplyType, invoice.WithholdingBase,
            invoice.WithholdingRatePercent, invoice.WithholdingAmount);
    }

    private static PaymentDto ToDto(Payment p) => new(p.Id, p.Name, p.Kind.ToString(), p.PartyId, p.PartyName, p.Allocations[0].InvoiceId,
        string.Join(", ", p.Allocations.Select(a => a.InvoiceNumber)), p.Amount, p.BankAccountCode, p.PostingDate, p.Reference, p.DocStatus.ToString(),
        p.Allocations.Select(a => new PaymentAllocationDto(a.InvoiceId, a.InvoiceNumber, a.Amount)).ToList());

    private async Task<Result<InvoiceDto>> NumberAndSaveAsync(Invoice invoice, string pattern, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        invoice.AssignName(await numbers.NextAsync(invoice.CompanyId, invoice.DocumentType, pattern, invoice.PostingDate, cancellationToken).ConfigureAwait(false));
        invoice.CreatedAt = clock.GetUtcNow();
        invoice.CreatedBy = currentUser.Username;
        invoices.Add(invoice);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(invoice);
    }

    public async Task<Result<InvoiceDto>> Handle(CreateReturnCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var original = await AccessibleAsync(command.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (original is null)
        {
            return Error.NotFound("Invoice", command.InvoiceId);
        }

        var lines = command.Lines is { Count: > 0 }
            ? command.Lines.Select(l => new InvoiceLine(l.Description, l.Quantity, l.Rate, l.AccountCode, l.IsService, l.VatApplicable, l.ItemId,
                original.Lines.FirstOrDefault(o => o.ItemId == l.ItemId)?.ItemCode)).ToList()
            : original.Lines.ToList();
        var allowed = original.Lines.Select(l => l.AccountCode).ToHashSet(StringComparer.Ordinal);
        if (lines.FirstOrDefault(l => !allowed.Contains(l.AccountCode)) is { } stray)
        {
            return Error.Validation($"Account {stray.AccountCode} is not on {original.Name}; a return reverses the invoice's own lines.");
        }

        Invoice note;
        try
        {
            note = Invoice.CreateReturn(original, command.PostingDate, lines, options.Rules);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        return await NumberAndSaveAsync(note, original.Kind == InvoiceKind.Sales ? "CN-.FY.-.#####" : "DN-.FY.-.#####", cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<InvoiceDto>> Handle(CreateOpeningInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        var party = await parties.GetAsync(command.PartyId, cancellationToken).ConfigureAwait(false);
        if (party is null || party.CompanyId != command.CompanyId)
        {
            return Error.NotFound(command.Kind == InvoiceKind.Sales ? "Customer" : "Supplier", command.PartyId);
        }

        Invoice invoice;
        try
        {
            invoice = Invoice.CreateOpening(command.Kind, command.CompanyId, party.Id, party.Name, party.Tin, command.PostingDate, command.DueDate ?? command.PostingDate,
                command.Amount, command.Reference);
            invoice.Submit();
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        return await NumberAndSaveAsync(invoice, command.Kind == InvoiceKind.Sales ? "SINV-.FY.-.#####" : "PINV-.FY.-.#####", cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<PaymentDto>>> Handle(ListInvoicePaymentsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await AccessibleAsync(query.InvoiceId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound("Invoice", query.InvoiceId);
        }

        return Result.Success<IReadOnlyList<PaymentDto>>((await invoices.PaymentsForInvoiceAsync(query.InvoiceId, cancellationToken).ConfigureAwait(false)).Select(ToDto).ToList());
    }

    /// <summary>Cancels a payment: its journal is reversed and the amount is owed on the invoice again.</summary>
    public async Task<Result<PaymentDto>> Handle(CancelPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var payment = await invoices.GetPaymentAsync(command.PaymentId, cancellationToken).ConfigureAwait(false);
        if (payment is null || !await access.CanAccessAsync(payment.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Payment", command.PaymentId);
        }

        try
        {
            var settled = new List<Invoice>();
            foreach (var allocation in payment.Allocations)
            {
                if (await invoices.GetAsync(allocation.InvoiceId, cancellationToken).ConfigureAwait(false) is { } settledInvoice)
                {
                    settledInvoice.ModifiedAt = clock.GetUtcNow();
                    settled.Add(settledInvoice);
                }
            }

            payment.CancelAgainst(settled);
            if (await journals.FindBySourceAsync(payment.Id, cancellationToken).ConfigureAwait(false) is { DocStatus: DocStatus.Submitted } journal)
            {
                journals.Add(journal.Reverse());
            }
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        payment.ModifiedAt = clock.GetUtcNow();
        payment.ModifiedBy = currentUser.Username;
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(payment);
    }

    /// <summary>
    /// What each customer owes (sales) or is owed to each supplier (purchases) on <see cref="AgeingReportQuery.AsOf"/>: every submitted invoice
    /// dated by then, less payments and returns dated by then, bucketed by days past the due date.
    /// </summary>
    public async Task<Result<AgeingReportDto>> Handle(AgeingReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var all = (await invoices.ListAsync(query.CompanyId, query.Kind, DateOnly.MinValue, query.AsOf, cancellationToken).ConfigureAwait(false))
            .Where(i => i.DocStatus == DocStatus.Submitted).ToList();
        var paid = (await invoices.PaymentsAsync(query.CompanyId, DateOnly.MinValue, query.AsOf, cancellationToken).ConfigureAwait(false))
            .Where(p => p.DocStatus == DocStatus.Submitted).SelectMany(p => p.Allocations).GroupBy(a => a.InvoiceId).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
        var returned = all.Where(i => i.IsReturn).GroupBy(i => i.ReturnAgainstId!.Value).ToDictionary(g => g.Key, g => g.Sum(i => i.PayableAmount));

        var open = all.Where(i => !i.IsReturn)
            .Select(i => (Invoice: i, Open: i.PayableAmount - paid.GetValueOrDefault(i.Id) - returned.GetValueOrDefault(i.Id)))
            .Where(x => x.Open != 0m).ToList();
        AgeingRow Row(Guid partyId, string name, string? tin, IEnumerable<(Invoice Invoice, decimal Open)> items)
        {
            decimal notDue = 0m, d30 = 0m, d60 = 0m, d90 = 0m, over = 0m;
            foreach (var (invoice, amount) in items)
            {
                var days = query.AsOf.DayNumber - invoice.DueDate.DayNumber;
                switch (days)
                {
                    case <= 0: notDue += amount; break;
                    case <= 30: d30 += amount; break;
                    case <= 60: d60 += amount; break;
                    case <= 90: d90 += amount; break;
                    default: over += amount; break;
                }
            }

            return new AgeingRow(partyId, name, tin, notDue, d30, d60, d90, over, notDue + d30 + d60 + d90 + over);
        }

        var rows = open.GroupBy(x => x.Invoice.PartyId)
            .Select(g => Row(g.Key, g.First().Invoice.PartyName, g.First().Invoice.PartyTin, g))
            .OrderByDescending(r => r.Total).ToList();
        return new AgeingReportDto(query.Kind.ToString(), query.AsOf, rows, Row(Guid.Empty, "Total", null, open));
    }
}
