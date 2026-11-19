using Gebeta.SharedKernel;

namespace Gebeta.Stock.Domain.Ledger;

/// <summary>What a landed cost charge is shared out by across the lines of the goods received.</summary>
public enum AllocationBasis
{
    /// <summary>The value each line was received at (quantity times rate).</summary>
    Value,
    Quantity,

    /// <summary>The weight given for each line on the landed cost (any one unit; only the shares matter).</summary>
    Weight,
    Volume,
}

/// <summary>One extra cost (freight, customs duty, clearing, LC bank charges), credited to <see cref="AccountCode"/>.</summary>
public sealed record LandedCharge(string Description, decimal Amount, AllocationBasis Basis, string AccountCode);

/// <summary>What the landed cost does to one line of the receipt: <see cref="Allocated"/> is added to the line's stock value.</summary>
public sealed record LandedCostLine(int ReceiptLineIndex, Guid ItemId, string ItemCode, string ItemName, decimal Qty, decimal ReceivedValue, decimal? Weight, decimal? Volume,
    decimal Allocated);

/// <summary>The weight and volume of one receipt line, given on the landed cost when a charge is shared by them.</summary>
public sealed record LandedCostLineInput(int ReceiptLineIndex, decimal? Weight, decimal? Volume);

/// <summary>
/// Extra costs on goods already received: freight, duty, clearing, LC charges. Submitting adds each charge to the stock value
/// of the receipt's lines, shared by value, quantity, weight or volume, so the item cost includes them (docs/modules/stock.md).
/// </summary>
public sealed class LandedCost : DocumentBase
{
    /// <summary>The account a charge is credited to unless another is given: the supplier's (forwarder's) invoice line debits it, as for goods received.</summary>
    public const string DefaultChargeAccount = StockEntry.StockReceivedNotBilled;

    private List<LandedCharge> _charges = [];
    private List<LandedCostLine> _lines = [];

    private LandedCost()
    {
        ReceiptNumber = string.Empty;
    }

    private LandedCost(Guid companyId, DateOnly postingDate)
        : base(companyId, postingDate)
    {
        ReceiptNumber = string.Empty;
    }

    public override string DocumentType => "Landed Cost";

    /// <summary>The submitted stock receipt the costs are added to.</summary>
    public Guid ReceiptId { get; private set; }

    public string ReceiptNumber { get; private set; }
    public string? Remark { get; private set; }
    public IReadOnlyList<LandedCharge> Charges => _charges;
    public IReadOnlyList<LandedCostLine> Lines => _lines;
    public decimal TotalCharges => _charges.Sum(c => c.Amount);

    public static LandedCost Create(StockEntry receipt, DateOnly postingDate, IReadOnlyList<LandedCharge> charges, IReadOnlyList<LandedCostLineInput>? lineInputs, string? remark)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(charges);
        if (receipt.Purpose != StockEntryPurpose.Receipt || receipt.DocStatus != DocStatus.Submitted)
        {
            throw new DomainException("landed.receipt", "Landed costs are added to a submitted stock receipt.");
        }

        if (postingDate < receipt.PostingDate)
        {
            throw new DomainException("landed.date", "A landed cost cannot be dated before the receipt it belongs to.");
        }

        if (charges.Count == 0)
        {
            throw new DomainException("landed.charges", "A landed cost needs at least one charge.");
        }

        var cleaned = new List<LandedCharge>();
        foreach (var c in charges)
        {
            if (string.IsNullOrWhiteSpace(c.Description) || c.Amount <= 0m || decimal.Round(c.Amount, 2) != c.Amount)
            {
                throw new DomainException("landed.charge", "Each charge needs a description and a positive amount with at most two decimals.");
            }

            cleaned.Add(c with { Description = c.Description.Trim(), AccountCode = string.IsNullOrWhiteSpace(c.AccountCode) ? DefaultChargeAccount : c.AccountCode.Trim() });
        }

        var inputs = (lineInputs ?? []).ToDictionary(i => i.ReceiptLineIndex);
        if (inputs.Keys.Any(i => i < 0 || i >= receipt.Lines.Count) || inputs.Values.Any(i => i.Weight < 0m || i.Volume < 0m))
        {
            throw new DomainException("landed.line", "Weights and volumes belong to a line of the receipt and cannot be negative.");
        }

        var lines = receipt.Lines.Select((l, i) => new LandedCostLine(i, l.ItemId, l.ItemCode, l.ItemName, l.Qty, Rounding.ToCurrency(l.Qty * l.Rate!.Value),
            inputs.GetValueOrDefault(i)?.Weight, inputs.GetValueOrDefault(i)?.Volume, 0m)).ToList();

        var allocated = new decimal[lines.Count];
        foreach (var charge in cleaned)
        {
            var basis = lines.Select(l => charge.Basis switch
            {
                AllocationBasis.Value => l.ReceivedValue,
                AllocationBasis.Quantity => l.Qty,
                AllocationBasis.Weight => l.Weight ?? 0m,
                _ => l.Volume ?? 0m,
            }).ToList();
            if (basis.Sum() <= 0m)
            {
                throw new DomainException("landed.basis", $"{charge.Description}: there is nothing to share it by; give the line {charge.Basis.ToString().ToLowerInvariant()}s.");
            }

            var shares = Split(charge.Amount, basis);
            for (var i = 0; i < shares.Count; i++)
            {
                allocated[i] += shares[i];
            }
        }

        return new LandedCost(receipt.CompanyId, postingDate)
        {
            ReceiptId = receipt.Id, ReceiptNumber = receipt.Name ?? string.Empty, _charges = cleaned,
            _lines = lines.Select((l, i) => l with { Allocated = allocated[i] }).ToList(),
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim(),
        };
    }

    /// <summary>
    /// Shares <paramref name="amount"/> in proportion to <paramref name="basis"/>, to the cent; the shares add up to the amount exactly.
    /// What rounding leaves over goes to the line with the largest basis (the first of equals).
    /// </summary>
    public static IReadOnlyList<decimal> Split(decimal amount, IReadOnlyList<decimal> basis)
    {
        ArgumentNullException.ThrowIfNull(basis);
        var total = basis.Sum();
        if (total <= 0m || basis.Any(b => b < 0m))
        {
            throw new DomainException("landed.basis", "Nothing to share the amount by.");
        }

        var shares = basis.Select(b => Rounding.ToCurrency(amount * b / total)).ToArray();
        var largest = 0;
        for (var i = 1; i < basis.Count; i++)
        {
            if (basis[i] > basis[largest])
            {
                largest = i;
            }
        }

        shares[largest] += amount - shares.Sum();
        return shares;
    }

    protected override void ValidateForSubmit()
    {
        if (_lines.Sum(l => l.Allocated) != TotalCharges)
        {
            throw new DomainException("landed.total", "The shares do not add up to the charges.");
        }
    }
}
