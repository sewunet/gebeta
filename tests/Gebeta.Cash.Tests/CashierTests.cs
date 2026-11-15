using Gebeta.Cash.Domain.Cashiers;
using Gebeta.SharedKernel;

namespace Gebeta.Cash.Tests;

public class CashierTests
{
    private static readonly Guid Company = Guid.CreateVersion7();
    private static readonly DateOnly Today = new(2026, 11, 16);

    private static Cashier New(CashierType type = CashierType.PettyCashier, bool autoTopUp = true, decimal? maxTransaction = null, decimal? maxWithdrawal = null) =>
        Cashier.Create(Company, "pc01", "Petty Cash Office", type, "1110", null, autoTopUp, maxTransaction, maxWithdrawal, null).Value;

    [Fact]
    public void A_cashier_cannot_pay_more_than_their_own_balance()
    {
        var cashier = New();
        cashier.Receive(1_000m, Today, CashMovementKind.Receipt, Guid.NewGuid(), "CR-1", "Float", isDeposit: true);

        var ex = Assert.Throws<DomainException>(() => cashier.Pay(1_000.01m, Today, CashMovementKind.Payment, Guid.NewGuid(), "CP-1", "Fuel"));

        Assert.Equal("cash.overdraw", ex.Code);
        Assert.Equal(1_000m, cashier.Balance);
        Assert.Equal(0m, cashier.Pay(1_000m, Today, CashMovementKind.Payment, Guid.NewGuid(), "CP-2", "Fuel").BalanceAfter);
    }

    [Fact]
    public void Limits_are_enforced()
    {
        var cashier = New(maxTransaction: 500m, maxWithdrawal: 300m);
        cashier.Receive(500m, Today, CashMovementKind.Receipt, Guid.NewGuid(), "CR-1", "Float", isDeposit: true);

        Assert.Equal("cash.limit_transaction", Assert.Throws<DomainException>(() =>
            cashier.Receive(501m, Today, CashMovementKind.Receipt, Guid.NewGuid(), "CR-2", "Too much", isDeposit: true)).Code);
        Assert.Equal("cash.limit_withdrawal", Assert.Throws<DomainException>(() =>
            cashier.Pay(400m, Today, CashMovementKind.TransferOut, Guid.NewGuid(), "CT-1", "Handover")).Code);
    }

    [Fact]
    public void Only_petty_cashiers_with_auto_top_up_get_automatic_requests()
    {
        var settings = CashSettings.Create(Company);
        settings.Update(enableAutoRequest: true, minimumBalance: 1_000m, targetBalance: 5_000m, "1110", "1120");

        var petty = New();
        var sales = New(CashierType.SalesCashier);
        var pettyManual = New(autoTopUp: false);

        Assert.Equal(4_500m, settings.TopUpFor(petty, 500m));
        Assert.Null(settings.TopUpFor(petty, 1_000m));
        Assert.Null(settings.TopUpFor(sales, 0m));
        Assert.False(sales.AutoTopUp);
        Assert.Null(settings.TopUpFor(pettyManual, 0m));
    }

    [Fact]
    public void A_request_is_approved_with_a_funding_source_or_rejected_with_a_reason()
    {
        var cashier = New();
        var request = PettyCashRequest.Create(cashier, 4_500m, Today, bySystem: true, null);
        request.AssignName("PCR-1");
        request.SubmitForApproval();
        Assert.Equal(PettyCashStatus.Pending, request.Status);

        Assert.Throws<DomainException>(() => request.MarkApproved(FundingFrom.Cashier, null, cashier.Id, "manager")); // the cashier itself
        request.MarkApproved(FundingFrom.SourceAccount, "1120", null, "manager");
        Assert.Equal(PettyCashStatus.ApprovedAndPaid, request.Status);

        var other = PettyCashRequest.Create(cashier, 100m, Today, bySystem: false, null);
        other.AssignName("PCR-2");
        other.SubmitForApproval();
        Assert.Throws<DomainException>(() => other.Reject(" "));
        other.Reject("Budget exhausted");
        Assert.Equal(PettyCashStatus.Rejected, other.Status);
    }
}
