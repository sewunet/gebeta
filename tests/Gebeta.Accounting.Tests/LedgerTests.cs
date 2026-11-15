using Gebeta.Accounting.Domain.Ledger;
using Gebeta.SharedKernel;

namespace Gebeta.Accounting.Tests;

public class LedgerTests
{
    private static readonly Guid Company = Guid.CreateVersion7();
    private static readonly Guid Cash = Guid.CreateVersion7();
    private static readonly Guid Rent = Guid.CreateVersion7();

    [Fact]
    public void An_unbalanced_journal_cannot_be_posted()
    {
        var journal = JournalEntry.Create(Company, new DateOnly(2026, 11, 30), "Rent", [new(Rent, 5_000m, 0m, null), new(Cash, 0m, 4_999.99m, null)]);
        journal.AssignName("JV-1");

        Assert.Equal("accounting.unbalanced", Assert.Throws<DomainException>(() => journal.Post()).Code);
        Assert.Equal(DocStatus.Draft, journal.DocStatus);
    }

    [Fact]
    public void Posting_writes_the_ledger_and_cancelling_writes_the_reversal()
    {
        var journal = JournalEntry.Create(Company, new DateOnly(2026, 11, 30), "Rent for Hidar", [new(Rent, 5_000m, 0m, null), new(Cash, 0m, 5_000m, null)]);
        journal.AssignName("JV-2");

        var posted = journal.Post();
        var reversed = journal.Reverse();

        Assert.Equal(2, posted.Count);
        Assert.Equal(posted.Sum(g => g.Debit), posted.Sum(g => g.Credit));
        Assert.All(reversed, g => Assert.True(g.IsReversal));
        Assert.Equal(0m, posted.Concat(reversed).Where(g => g.AccountId == Rent).Sum(g => g.Debit - g.Credit));
        Assert.Equal(DocStatus.Cancelled, journal.DocStatus);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void A_journal_needs_two_one_sided_lines(int variant)
    {
        JournalLine[] lines = variant == 1
            ? [new(Rent, 100m, 0m, null)]
            : [new(Rent, 100m, 100m, null), new(Cash, 0m, 0m, null), new(Cash, 0m, 100m, null)];

        Assert.Throws<DomainException>(() => JournalEntry.Create(Company, new DateOnly(2026, 11, 30), "x", lines));
    }

    [Fact]
    public void The_ethiopian_chart_is_consistent_and_has_the_payroll_accounts()
    {
        var accounts = EthiopianChartOfAccounts.Accounts;
        Assert.Equal(accounts.Count, accounts.Select(a => a.Code).Distinct().Count());
        Assert.All(accounts.Where(a => a.ParentCode is not null), a => Assert.Contains(accounts, p => p.Code == a.ParentCode && p.IsGroup));
        foreach (var code in new[] { EthiopianChartOfAccounts.IncomeTaxPayable, EthiopianChartOfAccounts.PensionPayable, EthiopianChartOfAccounts.SalaryPayable, EthiopianChartOfAccounts.SalaryExpense })
        {
            Assert.Contains(accounts, a => a.Code == code && !a.IsGroup);
        }
    }
}
