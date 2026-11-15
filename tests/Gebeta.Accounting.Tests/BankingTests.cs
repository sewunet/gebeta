using Gebeta.Accounting.Domain.Banking;
using Gebeta.Accounting.Domain.Ledger;
using Gebeta.SharedKernel;

namespace Gebeta.Accounting.Tests;

public class BankingTests
{
    private static readonly Guid Company = Guid.CreateVersion7();
    private static readonly Account Ledger = Account.Create(Company, "1121", "Bank - CBE Main", RootType.Asset, false, null).Value;
    private static readonly Guid Other = Guid.CreateVersion7();
    private static readonly BankAccount Bank = BankAccount.Create(Company, "Main", "Commercial Bank of Ethiopia", "1000123456789", Ledger).Value;
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    /// <summary>Ledger rows on the bank account: + money in, − money out.</summary>
    private static GlEntry Entry(DateOnly date, decimal amount)
    {
        var lines = amount > 0
            ? new JournalLine[] { new(Ledger.Id, amount, 0m, null), new(Other, 0m, amount, null) }
            : [new(Other, -amount, 0m, null), new(Ledger.Id, 0m, -amount, null)];
        var journal = JournalEntry.Create(Company, date, "Test", lines);
        journal.AssignName($"JV-{Guid.NewGuid():N}");
        return journal.Post().Single(e => e.AccountId == Ledger.Id);
    }

    private static BankStatementLine Line(DateOnly date, decimal amount, string? reference = null) =>
        BankStatementLine.Create(Bank, date, "Statement", reference, amount, null, Guid.Empty);

    private static readonly DateOnly Day = new(2026, 11, 14);

    [Fact]
    public void A_bank_account_needs_a_postable_asset_account()
    {
        var group = Account.Create(Company, "1100", "Cash and Bank", RootType.Asset, true, null).Value;
        var expense = Account.Create(Company, "6200", "Rent", RootType.Expense, false, null).Value;
        Assert.True(BankAccount.Create(Company, "Main", "CBE", "1", group).IsFailure);
        Assert.True(BankAccount.Create(Company, "Main", "CBE", "1", expense).IsFailure);
        Assert.True(BankAccount.Create(Company, "", "CBE", "1", Ledger).IsFailure);
    }

    [Fact]
    public void A_match_must_add_up_to_the_statement_line()
    {
        var line = Line(Day, -5_000m);
        Assert.Equal("accounting.match_amount", Assert.Throws<DomainException>(() => BankMatch.Match(line, [Entry(Day, -4_999m)], "a", Now)).Code);

        var matches = BankMatch.Match(line, [Entry(Day, -2_000m), Entry(Day, -3_000m)], "a", Now); // one cheque paying two invoices
        Assert.Equal(2, matches.Count);
        Assert.All(matches, m => Assert.Equal(line.Id, m.StatementLineId));
    }

    [Fact]
    public void Suggestions_pair_only_unambiguous_lines_within_three_days()
    {
        var deposit = Line(Day, 100_000m);
        var twinA = Line(Day, -2_500m);
        var twinB = Line(Day.AddDays(1), -2_500m);
        var late = Line(Day, -700m);
        var entries = new[] { Entry(Day.AddDays(-2), 100_000m), Entry(Day, -2_500m), Entry(Day.AddDays(1), -2_500m), Entry(Day.AddDays(-4), -700m) };

        var pairs = BankMatching.Suggest([deposit, twinA, twinB, late], entries);

        var pair = Assert.Single(pairs);
        Assert.Same(deposit, pair.Line);
        Assert.Equal(100_000m, BankMatch.BankAmount(pair.Entry));
    }

    [Fact]
    public void The_same_statement_line_is_recognised_on_a_second_import()
    {
        var line = Line(Day, -8_000m, "CHQ001");
        Assert.True(line.SameAs(Day, -8_000m, "chq001", "anything"));
        Assert.False(line.SameAs(Day, -8_000m, "CHQ002", "Statement"));
        Assert.True(Line(Day, -150m).SameAs(Day, -150m, null, "statement"));
        Assert.Throws<DomainException>(() => Line(Day, 0m));
    }
}
