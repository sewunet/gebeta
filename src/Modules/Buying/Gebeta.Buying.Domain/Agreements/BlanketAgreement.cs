using Gebeta.Buying.Domain.Orders;
using Gebeta.SharedKernel;

namespace Gebeta.Buying.Domain.Agreements;

/// <summary>An item on a framework agreement at its agreed rate, with the quantity planned (none when only the agreement's amount is capped).</summary>
public sealed record AgreementLine(Guid ItemId, string ItemCode, string ItemName, string Uom, decimal Rate, decimal? PlannedQty);

/// <summary>What submitted purchase orders have called off of one item so far.</summary>
public sealed record CalledOff(Guid ItemId, decimal Qty, decimal Amount);

public enum AgreementState
{
    Draft,
    Active,
    Exhausted,
    Expired,
    Cancelled,
}

/// <summary>
/// A framework (blanket) agreement with a supplier: items at agreed rates over a period, capped by a planned quantity per item
/// and/or an amount for the whole agreement. Purchase orders call off against it; what they have taken is never stored here but
/// worked out from the submitted orders that name the agreement, so cancelling an order gives its share back.
/// </summary>
public sealed class BlanketAgreement : DocumentBase
{
    private List<AgreementLine> _lines = [];

    private BlanketAgreement()
    {
        SupplierName = string.Empty;
    }

    private BlanketAgreement(Guid companyId, DateOnly agreementDate)
        : base(companyId, agreementDate)
    {
        SupplierName = string.Empty;
    }

    public override string DocumentType => "Blanket Agreement";

    public Guid SupplierId { get; private set; }
    public string SupplierName { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    public DateOnly ValidTo { get; private set; }

    /// <summary>The most the agreement may call off in total, when it is capped by amount.</summary>
    public decimal? AmountLimit { get; private set; }

    public string? Remark { get; private set; }
    public IReadOnlyList<AgreementLine> Lines => _lines;

    public static BlanketAgreement Create(Guid companyId, Guid supplierId, string supplierName, DateOnly agreementDate, DateOnly validFrom, DateOnly validTo,
        decimal? amountLimit, IReadOnlyList<AgreementLine> lines, string? remark)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0 || lines.Any(l => l.Rate < 0m || l.PlannedQty is <= 0m))
        {
            throw new DomainException("buying.agreement_lines", "An agreement needs items with a rate, and a planned quantity above zero where one is given.");
        }

        if (lines.Select(l => l.ItemId).Distinct().Count() != lines.Count)
        {
            throw new DomainException("buying.agreement_duplicate", "Each item appears once on an agreement.");
        }

        if (validTo < validFrom)
        {
            throw new DomainException("buying.agreement_period", "The agreement cannot end before it starts.");
        }

        if (amountLimit is <= 0m)
        {
            throw new DomainException("buying.agreement_limit", "The agreed amount must be above zero.");
        }

        if (amountLimit is null && lines.Any(l => l.PlannedQty is null))
        {
            throw new DomainException("buying.agreement_cap", "Give the agreement an amount, or a planned quantity on every item, so there is something to call off against.");
        }

        return new BlanketAgreement(companyId, agreementDate)
        {
            SupplierId = supplierId, SupplierName = supplierName, ValidFrom = validFrom, ValidTo = validTo, AmountLimit = amountLimit,
            _lines = lines.ToList(), Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim(),
        };
    }

    /// <summary>What a set of orders has taken, per item, from the order lines.</summary>
    public static IReadOnlyList<CalledOff> Summarise(IEnumerable<PurchaseOrder> orders) =>
        orders.SelectMany(o => o.Lines).GroupBy(l => l.ItemId).Select(g => new CalledOff(g.Key, g.Sum(l => l.Qty), g.Sum(l => l.Amount))).ToList();

    /// <summary>Remaining quantity of a line (null when the line has no planned quantity), never below zero.</summary>
    public static decimal? RemainingQty(AgreementLine line, IReadOnlyCollection<CalledOff> calledOff) =>
        line.PlannedQty is { } planned ? Math.Max(0m, planned - calledOff.Where(c => c.ItemId == line.ItemId).Sum(c => c.Qty)) : null;

    /// <summary>Remaining amount of the agreement (null when it has no amount limit), never below zero.</summary>
    public decimal? RemainingAmount(IReadOnlyCollection<CalledOff> calledOff) =>
        AmountLimit is { } limit ? Math.Max(0m, limit - calledOff.Sum(c => c.Amount)) : null;

    /// <summary>
    /// Refuses an order that may not call off here: the agreement must be submitted, the supplier and period must match, each line must
    /// be an agreed item at the agreed rate, and nothing may exceed what remains. <paramref name="already"/> is what other orders hold.
    /// </summary>
    public void CheckCallOff(Guid supplierId, DateOnly orderDate, IReadOnlyList<PurchaseOrderLine> lines, IReadOnlyCollection<CalledOff> already)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(already);
        var label = Name ?? DocumentType;
        if (DocStatus != DocStatus.Submitted)
        {
            throw new DomainException("buying.agreement_not_active", $"Agreement {label} is not submitted, so orders cannot call off against it.");
        }

        if (supplierId != SupplierId)
        {
            throw new DomainException("buying.agreement_supplier", $"Agreement {label} is with {SupplierName}; the order is for a different supplier.");
        }

        if (orderDate < ValidFrom || orderDate > ValidTo)
        {
            throw new DomainException("buying.agreement_period_outside", $"Agreement {label} runs from {ValidFrom:yyyy-MM-dd} to {ValidTo:yyyy-MM-dd}; the order date {orderDate:yyyy-MM-dd} is outside it.");
        }

        foreach (var l in lines)
        {
            var agreed = _lines.FirstOrDefault(a => a.ItemId == l.ItemId)
                ?? throw new DomainException("buying.agreement_item", $"{l.ItemCode} is not on agreement {label}.");
            if (l.Rate != agreed.Rate)
            {
                throw new DomainException("buying.agreement_rate", $"{l.ItemCode} is agreed at {agreed.Rate:0.##} on {label}; the order has {l.Rate:0.##}.");
            }

            if (RemainingQty(agreed, already) is { } remaining && l.Qty > remaining)
            {
                throw new DomainException("buying.agreement_overdrawn", $"{l.ItemCode}: {l.Qty:0.###} is more than the {remaining:0.###} left on agreement {label}.");
            }
        }

        if (RemainingAmount(already) is { } amountLeft && lines.Sum(l => l.Amount) > amountLeft)
        {
            throw new DomainException("buying.agreement_overdrawn", $"The order is for {lines.Sum(l => l.Amount):0.##}, more than the {amountLeft:0.##} left on agreement {label}.");
        }
    }

    /// <summary>Where the agreement stands today, given what has been called off.</summary>
    public AgreementState StateFor(DateOnly today, IReadOnlyCollection<CalledOff> calledOff)
    {
        if (DocStatus == DocStatus.Draft)
        {
            return AgreementState.Draft;
        }

        if (DocStatus == DocStatus.Cancelled)
        {
            return AgreementState.Cancelled;
        }

        if (today > ValidTo)
        {
            return AgreementState.Expired;
        }

        var amountDone = RemainingAmount(calledOff) is 0m;
        var quantitiesDone = _lines.All(l => l.PlannedQty is not null) && _lines.All(l => RemainingQty(l, calledOff) is 0m);
        return amountDone || quantitiesDone ? AgreementState.Exhausted : AgreementState.Active;
    }
}

public interface IBlanketAgreementRepository
{
    Task<BlanketAgreement?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Reads the agreement holding a row lock until the transaction ends, so two orders cannot overdraw it together. Needs a transaction.</summary>
    Task<BlanketAgreement?> GetLockedAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<BlanketAgreement>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    Task<IReadOnlyList<BlanketAgreement>> ListAsync(Guid companyId, CancellationToken cancellationToken);
    void Add(BlanketAgreement agreement);
}
