using Gebeta.Accounting.Contracts;
using Gebeta.Accounting.Domain.Ledger;
using Gebeta.Application;
using Gebeta.SharedKernel;

namespace Gebeta.Accounting.Application;

public sealed record ComparativeProfitAndLossQuery(Guid CompanyId, DateOnly From, DateOnly To, ReportComparison Compare) : IQuery<ComparativeReportDto>;

public sealed record ComparativeBalanceSheetQuery(Guid CompanyId, DateOnly AsOf, ReportComparison Compare) : IQuery<ComparativeReportDto>;

/// <summary>
/// Profit and loss and the balance sheet next to the previous period or last year (docs/requirements/competitive-features.md #3). The
/// existing reports run twice, for the dates <see cref="ComparisonPeriods"/> works out from the company's fiscal calendar, and are merged
/// by account, so each column is exactly the plain report for its dates. Computed live, never stored.
/// </summary>
public sealed class ComparativeReportsHandler(
    IQueryHandler<ProfitAndLossQuery, ProfitAndLossDto> profitAndLoss,
    IQueryHandler<BalanceSheetQuery, BalanceSheetDto> balanceSheet,
    ICompanyDirectory companies,
    ICompanyAccess access)
    : IQueryHandler<ComparativeProfitAndLossQuery, ComparativeReportDto>, IQueryHandler<ComparativeBalanceSheetQuery, ComparativeReportDto>
{
    private const string NothingToCompare = "No postings in the comparison period; it may be before the company's first entries in Gebeta.";

    public async Task<Result<ComparativeReportDto>> Handle(ComparativeProfitAndLossQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await EthiopianAsync(query.CompanyId, cancellationToken).ConfigureAwait(false) is not { } ethiopian)
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        if (query.Compare == ReportComparison.None || query.To < query.From)
        {
            return Error.Validation("Choose what to compare with, and an end date on or after the start date.");
        }

        var (current, comparison) = ComparisonPeriods.ForRange(query.From, query.To, query.Compare, ethiopian);
        var now = await profitAndLoss.Handle(new ProfitAndLossQuery(query.CompanyId, current.From, current.To), cancellationToken).ConfigureAwait(false);
        var then = await profitAndLoss.Handle(new ProfitAndLossQuery(query.CompanyId, comparison.From, comparison.To), cancellationToken).ConfigureAwait(false);
        if (now.IsFailure || then.IsFailure)
        {
            return (now.Error ?? then.Error)!;
        }

        var a = now.Value;
        var b = then.Value;
        return new ComparativeReportDto(query.CompanyId, "ProfitAndLoss", query.Compare.ToString(), current.From, current.To, comparison.From, comparison.To,
            [Section("Income", a.Income, b.Income, a.TotalIncome, b.TotalIncome), Section("Expenses", a.Expenses, b.Expenses, a.TotalExpenses, b.TotalExpenses)],
            [Row("", "Net profit", a.NetProfit, b.NetProfit)], true, true,
            b.Income.Count + b.Expenses.Count == 0 ? NothingToCompare : null);
    }

    public async Task<Result<ComparativeReportDto>> Handle(ComparativeBalanceSheetQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await EthiopianAsync(query.CompanyId, cancellationToken).ConfigureAwait(false) is not { } ethiopian)
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        if (query.Compare is not (ReportComparison.PreviousPeriod or ReportComparison.LastYear))
        {
            return Error.Validation("A balance sheet compares with the previous period or the same date last year.");
        }

        var thenDate = ComparisonPeriods.ForDate(query.AsOf, query.Compare, ethiopian);
        var now = await balanceSheet.Handle(new BalanceSheetQuery(query.CompanyId, query.AsOf), cancellationToken).ConfigureAwait(false);
        var then = await balanceSheet.Handle(new BalanceSheetQuery(query.CompanyId, thenDate), cancellationToken).ConfigureAwait(false);
        if (now.IsFailure || then.IsFailure)
        {
            return (now.Error ?? then.Error)!;
        }

        var a = now.Value;
        var b = then.Value;
        return new ComparativeReportDto(query.CompanyId, "BalanceSheet", query.Compare.ToString(), query.AsOf, query.AsOf, thenDate, thenDate,
            [Section("Assets", a.Assets, b.Assets, a.TotalAssets, b.TotalAssets), Section("Liabilities", a.Liabilities, b.Liabilities, a.TotalLiabilities, b.TotalLiabilities),
             Section("Equity", a.Equity, b.Equity, a.TotalEquity, b.TotalEquity)],
            [Row("", "Profit not yet closed", a.UnclosedProfit, b.UnclosedProfit)], a.Balanced, b.Balanced,
            b.Assets.Count + b.Liabilities.Count + b.Equity.Count == 0 ? NothingToCompare : null);
    }

    private async Task<bool?> EthiopianAsync(Guid companyId, CancellationToken cancellationToken) =>
        await access.CanAccessAsync(companyId, cancellationToken).ConfigureAwait(false)
        && await companies.GetAsync(companyId, cancellationToken).ConfigureAwait(false) is { } company
            ? company.UsesEthiopianFiscalYear
            : null;

    /// <summary>Every account with an amount in either column, matched by account code (codes do not change when an account is renamed).</summary>
    private static ComparativeSection Section(string name, IReadOnlyList<ReportRow> now, IReadOnlyList<ReportRow> then, decimal totalNow, decimal totalThen)
    {
        var current = now.ToDictionary(r => r.Code, StringComparer.Ordinal);
        var previous = then.ToDictionary(r => r.Code, StringComparer.Ordinal);
        var rows = current.Keys.Union(previous.Keys).Order(StringComparer.Ordinal)
            .Select(code => Row(code, (current.GetValueOrDefault(code) ?? previous[code]).Name, current.GetValueOrDefault(code)?.Amount, previous.GetValueOrDefault(code)?.Amount))
            .ToList();
        return new ComparativeSection(name, rows, Row("", $"Total {name.ToLowerInvariant()}", totalNow, totalThen));
    }

    private static ComparativeRow Row(string code, string name, decimal? now, decimal? then)
    {
        var change = (now ?? 0m) - (then ?? 0m);
        return new ComparativeRow(code, name, now, then, change, then is { } t && t != 0m ? Math.Round(change / Math.Abs(t) * 100m, 1) : null);
    }
}
