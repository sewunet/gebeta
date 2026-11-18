using Gebeta.Accounting.Contracts;
using Gebeta.Accounting.Domain.Invoicing;
using Gebeta.Application;
using Gebeta.SharedKernel;

namespace Gebeta.Accounting.Application;

public sealed record PartyStatementQuery(Guid CompanyId, Guid PartyId, InvoiceKind Kind, DateOnly From, DateOnly To) : IQuery<PartyStatementDto>;

/// <summary>
/// A statement of account for one customer (sales) or supplier (purchases): the opening balance, every submitted invoice, withholding,
/// credit or debit note and payment in the range with a running balance, the closing balance, and the ageing of what is open at the end
/// (docs/requirements/competitive-features.md #4). Built from the documents themselves, because ledger rows carry no party; the closing
/// balance therefore always equals the party's ageing total. Cancelled documents are left out: their ledger rows cancel out.
/// </summary>
public sealed class PartyStatementHandler(
    IInvoiceRepository invoices,
    IPartyDirectory parties,
    ICompanyAccess access,
    IQueryHandler<AgeingReportQuery, AgeingReportDto> ageing)
    : IQueryHandler<PartyStatementQuery, PartyStatementDto>
{
    public const string Opening = "Opening balance";
    public const string Invoice = "Invoice";
    public const string Withholding = "Withholding";
    public const string Return = "Credit note";
    public const string WithholdingReversed = "Withholding reversed";
    public const string Payment = "Payment";

    public async Task<Result<PartyStatementDto>> Handle(PartyStatementQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.To < query.From)
        {
            return Error.Validation("The end date is before the start date.");
        }

        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var party = await parties.GetAsync(query.PartyId, cancellationToken).ConfigureAwait(false);
        if (party is null || party.CompanyId != query.CompanyId)
        {
            return Error.NotFound("Party", query.PartyId);
        }

        var paymentKind = query.Kind == InvoiceKind.Sales ? PaymentKind.Receive : PaymentKind.Pay;
        var documents = (await invoices.ForPartyAsync(query.CompanyId, query.PartyId, query.To, cancellationToken).ConfigureAwait(false))
            .Where(i => i.Kind == query.Kind && i.DocStatus == DocStatus.Submitted)
            .SelectMany(Lines);
        var payments = (await invoices.PaymentsForPartyAsync(query.CompanyId, query.PartyId, query.To, cancellationToken).ConfigureAwait(false))
            .Where(p => p.Kind == paymentKind && p.DocStatus == DocStatus.Submitted)
            .Select(p => (Order: 5, Line: new PartyStatementLine(p.PostingDate, Payment, p.Name, p.Id, Join(p.Reference, string.Join(", ", p.Allocations.Select(a => a.InvoiceNumber))),
                null, Owed(-p.Amount).Debit, Owed(-p.Amount).Credit, 0m)));

        // Positive "owed" amounts raise the balance: a debit on a customer, a credit on a supplier.
        (decimal Debit, decimal Credit) Owed(decimal amount) => (amount >= 0m) == (query.Kind == InvoiceKind.Sales) ? (Math.Abs(amount), 0m) : (0m, Math.Abs(amount));
        decimal Signed(PartyStatementLine l) => query.Kind == InvoiceKind.Sales ? l.Debit - l.Credit : l.Credit - l.Debit;

        IEnumerable<(int Order, PartyStatementLine Line)> Lines(Invoice i)
        {
            PartyStatementLine Line(string type, decimal owed, string? reference, DateOnly? due = null) =>
                new(i.PostingDate, type, i.Name, i.Id, reference, due, Owed(owed).Debit, Owed(owed).Credit, 0m);

            if (i.IsReturn)
            {
                yield return (3, Line(Return, -i.GrandTotal, i.ReturnAgainstNumber));
                if (i.WithholdingAmount != 0m)
                {
                    yield return (4, Line(WithholdingReversed, i.WithholdingAmount, i.ReturnAgainstNumber));
                }

                yield break;
            }

            yield return (i.IsOpening ? 0 : 1, Line(i.IsOpening ? Opening : Invoice, i.GrandTotal, Join(i.SupplierInvoiceNumber, i.OrderReference), i.DueDate));
            if (i.WithholdingAmount != 0m)
            {
                yield return (2, Line(Withholding, -i.WithholdingAmount, i.WithholdingReceiptNumber));
            }
        }

        var all = documents.Concat(payments)
            .OrderBy(x => x.Line.Date).ThenBy(x => x.Order).ThenBy(x => x.Line.Number, StringComparer.Ordinal).Select(x => x.Line).ToList();
        var opening = all.Where(l => l.Date < query.From).Sum(Signed);
        var running = opening;
        var lines = new List<PartyStatementLine>();
        foreach (var line in all.Where(l => l.Date >= query.From))
        {
            running += Signed(line);
            lines.Add(line with { Balance = running });
        }

        var aged = await ageing.Handle(new AgeingReportQuery(query.CompanyId, query.Kind, query.To), cancellationToken).ConfigureAwait(false);
        if (aged.IsFailure)
        {
            return aged.Error!;
        }

        var partyAgeing = aged.Value.Rows.FirstOrDefault(r => r.PartyId == query.PartyId) ?? new AgeingRow(party.Id, party.Name, party.Tin, 0m, 0m, 0m, 0m, 0m, 0m);
        return new PartyStatementDto(query.CompanyId, party.Id, party.Name, party.Tin, query.Kind.ToString(), query.From, query.To, opening, lines,
            lines.Sum(l => l.Debit), lines.Sum(l => l.Credit), running, partyAgeing);
    }

    private static string? Join(params string?[] parts)
    {
        var text = string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return text.Length == 0 ? null : text;
    }
}
