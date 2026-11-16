using Gebeta.Modularity;

namespace Gebeta.Composition;

/// <summary>Every module this build contains. Which of them run is decided by <c>Modules:Enabled</c>.</summary>
public static class ModuleCatalogue
{
    public static IErpModule[] All() =>
    [
        new Gebeta.Core.Infrastructure.CoreModule(),
        new Gebeta.Payroll.Infrastructure.PayrollModule(),
        new Gebeta.Hr.Infrastructure.HrModule(),
        new Gebeta.Accounting.Infrastructure.AccountingModule(),
        new Gebeta.Cash.Infrastructure.CashModule(),
        new Gebeta.Stock.Infrastructure.StockModule(),
        new Gebeta.Buying.Infrastructure.BuyingModule(),
        new Gebeta.Selling.Infrastructure.SellingModule(),
    ];
}
