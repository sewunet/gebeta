using Gebeta.SharedKernel;
using Gebeta.SharedKernel.Ethiopia;

namespace Gebeta.Accounting.Domain.Ledger;

/// <summary>
/// An accounting month: one of the fiscal year's 12 Ethiopian periods (<see cref="EthiopianFiscalYear.MonthlyPeriods"/>,
/// where Nehase takes the Pagume days) or a Gregorian calendar month.
/// </summary>
public sealed record AccountingPeriod(DateOnly Start, DateOnly End, string Label)
{
    public static AccountingPeriod Containing(DateOnly date, bool ethiopian)
    {
        if (ethiopian)
        {
            var (month, from, to) = EthiopianFiscalYear.Containing(date).MonthlyPeriods().Single(p => date >= p.From && date <= p.To);
            return new AccountingPeriod(from, to, $"{EthiopianMonthNames.English(month)} {EthiopianDate.FromGregorian(from).Year}");
        }

        var first = new DateOnly(date.Year, date.Month, 1);
        return new AccountingPeriod(first, first.AddMonths(1).AddDays(-1), first.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture));
    }
}

public enum PeriodClosingStatus
{
    Closed,
    Reopened,
}

/// <summary>
/// A closed month. Nothing can be posted on or before the end of the latest closed month (the lock date).
/// Keeps the signed-off trial balance totals and profit of the month.
/// </summary>
public sealed class PeriodClosing : Entity
{
    private PeriodClosing()
    {
        Label = string.Empty;
        ClosedBy = string.Empty;
    }

    public Guid CompanyId { get; private set; }
    public string Label { get; private set; }
    public DateOnly Start { get; private set; }
    public DateOnly End { get; private set; }
    public PeriodClosingStatus Status { get; private set; }
    public decimal TotalDebit { get; private set; }
    public decimal TotalCredit { get; private set; }
    public decimal NetProfit { get; private set; }
    public DateTimeOffset ClosedAt { get; private set; }
    public string ClosedBy { get; private set; }
    public DateTimeOffset? ReopenedAt { get; private set; }
    public string? ReopenedBy { get; private set; }
    public string? ReopenReason { get; private set; }

    /// <param name="lockDate">End of the latest closed month, if any. Months close in order.</param>
    public static PeriodClosing Close(Guid companyId, AccountingPeriod period, DateOnly? lockDate, decimal totalDebit, decimal totalCredit, decimal netProfit,
        string closedBy, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(period);
        if (lockDate is { } locked && period.End <= locked)
        {
            throw new DomainException("accounting.period_already_closed", $"{period.Label} is already closed.");
        }

        if (lockDate is { } last && period.Start != last.AddDays(1))
        {
            throw new DomainException("accounting.period_order", $"Months close in order; close the month after {last:yyyy-MM-dd} first.");
        }

        if (totalDebit != totalCredit)
        {
            throw new DomainException("accounting.unbalanced", $"The trial balance does not balance ({totalDebit} / {totalCredit}).");
        }

        return new PeriodClosing
        {
            CompanyId = companyId, Label = period.Label, Start = period.Start, End = period.End, Status = PeriodClosingStatus.Closed,
            TotalDebit = totalDebit, TotalCredit = totalCredit, NetProfit = netProfit, ClosedAt = at, ClosedBy = closedBy, CreatedAt = at, CreatedBy = closedBy,
        };
    }

    public void Reopen(string by, DateTimeOffset at, string reason)
    {
        if (Status != PeriodClosingStatus.Closed)
        {
            throw new DomainException("accounting.period_not_closed", $"{Label} is not closed.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("accounting.reopen_reason", "Give a reason for reopening the month.");
        }

        Status = PeriodClosingStatus.Reopened;
        ReopenedAt = at;
        ReopenedBy = by;
        ReopenReason = reason.Trim();
        ModifiedAt = at;
        ModifiedBy = by;
    }
}

public interface IPeriodClosingRepository
{
    /// <summary>The latest month still closed, whose end is the lock date.</summary>
    Task<PeriodClosing?> LatestClosedAsync(Guid companyId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PeriodClosing>> ListAsync(Guid companyId, CancellationToken cancellationToken);
    void Add(PeriodClosing closing);
}
