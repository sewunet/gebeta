using System.Net;
using System.Net.Http.Json;
using Gebeta.Accounting.Contracts;
using Gebeta.Application;
using Gebeta.Buying.Contracts;
using Gebeta.Core.Contracts;
using Gebeta.Payroll.Contracts;
using Gebeta.Stock.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>Related documents (docs/requirements/sap-features.md #2): the chain around a document, from every installed module.</summary>
public class RelatedDocumentsTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task An_order_its_receipt_invoice_payment_and_journals_point_at_each_other()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Flow PLC", Ct);
        var id = company.Id;
        await admin.PostAsync(U($"/api/accounting/companies/{id}/chart"), null, Ct);
        var supplier = (await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = id, name = "Flow Supplier", type = "Supplier" }, Ct)).Content.ReadFromJsonAsync<PartyDto>(Ct))!;
        var cement = (await (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = id, code = "CEM", name = "Cement", uom = "bag", isStockItem = true }, Ct)).Content.ReadFromJsonAsync<ItemDto>(Ct))!;
        var store = (await (await admin.PostAsJsonAsync(U("/api/stock/warehouses"), new { companyId = id, code = "STORE", name = "Store" }, Ct)).Content.ReadFromJsonAsync<WarehouseDto>(Ct))!;

        var order = (await (await admin.PostAsJsonAsync(U("/api/buying/purchase-orders"), new
        {
            companyId = id, supplierId = supplier.Id, orderDate = "2026-11-10", lines = new[] { new { itemId = cement.Id, qty = 10m, rate = 500m, warehouseId = store.Id } },
        }, Ct)).Content.ReadFromJsonAsync<PurchaseOrderDto>(Ct))!;
        (await admin.PostAsync(U($"/api/buying/purchase-orders/{order.Id}/submit"), null, Ct)).EnsureSuccessStatusCode();

        var receipt = (await (await admin.PostAsJsonAsync(U("/api/stock/entries"), new
        {
            companyId = id, purpose = "Receipt", postingDate = "2026-11-12", partyId = supplier.Id, reference = order.Number,
            lines = new[] { new { itemId = cement.Id, qty = 10m, rate = 500m, targetWarehouseId = store.Id } },
        }, Ct)).Content.ReadFromJsonAsync<StockEntryDto>(Ct))!;
        (await admin.PostAsync(U($"/api/stock/entries/{receipt.Id}/submit"), null, Ct)).EnsureSuccessStatusCode();

        var bill = (await (await admin.PostAsJsonAsync(U("/api/accounting/invoices"), new
        {
            kind = "Purchase", companyId = id, partyId = supplier.Id, postingDate = "2026-11-14", orderReference = order.Number,
            lines = new[] { new { itemId = cement.Id, description = "Cement", quantity = 10, rate = 500, accountCode = "2150", isService = false } },
        }, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct))!;
        (await admin.PostAsync(U($"/api/accounting/invoices/{bill.Id}/submit"), null, Ct)).EnsureSuccessStatusCode();
        var payment = (await (await admin.PostAsJsonAsync(U("/api/accounting/payments"), new { invoiceId = bill.Id, amount = 1_000m, postingDate = "2026-11-20" }, Ct))
            .Content.ReadFromJsonAsync<PaymentDto>(Ct))!;

        async Task<List<RelatedDocument>> Related(Guid docId, string? number = null) =>
            (await admin.GetFromJsonAsync<List<RelatedDocument>>(U($"/api/documents/{docId}/related?companyId={id}{(number is null ? "" : $"&number={number}")}"), Ct))!;

        // The order: its receipt and invoice, found by the order number they carry.
        var forOrder = await Related(order.Id, order.Number);
        Assert.Contains(forOrder, d => d is { Type: "StockEntry" } && d.Id == receipt.Id);
        Assert.Contains(forOrder, d => d is { Type: "Invoice" } && d.Id == bill.Id);

        // The invoice: its order (the number made into a link), its payment and its journal.
        var forBill = await Related(bill.Id, bill.Number);
        Assert.Contains(forBill, d => d.Type == "PurchaseOrder" && d.Id == order.Id && d.Number == order.Number);
        Assert.Contains(forBill, d => d.Type == "Payment" && d.Id == payment.Id);
        var journal = Assert.Single(forBill, d => d.Type == "JournalEntry");

        // The payment: its invoice; the journal: the invoice it came from; the receipt: its order.
        Assert.Contains(await Related(payment.Id, payment.Number), d => d.Type == "Invoice" && d.Id == bill.Id);
        Assert.Contains(await Related(journal.Id!.Value, journal.Number), d => d.Type == "Invoice" && d.Id == bill.Id);
        Assert.Contains(await Related(receipt.Id, receipt.Number), d => d.Type == "PurchaseOrder" && d.Id == order.Id);

        using var stranger = factory.ClientFor("related.stranger");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(U($"/api/documents/{bill.Id}/related?companyId={id}"), Ct)).StatusCode);
    }

    [Fact]
    public async Task A_loan_and_the_salary_slip_that_deducted_it_point_at_each_other()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Flow Payroll PLC", Ct);
        var structure = (await (await admin.PostAsJsonAsync(U("/api/payroll/structures"), new { companyId = company.Id, name = "Staff" }, Ct)).Content.ReadFromJsonAsync<SalaryStructureDto>(Ct))!;
        var employee = (await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = company.Id, firstName = "Almaz", fatherName = "Test", gender = "Female", dateOfJoining = "2025-01-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct))!;
        await admin.PostAsJsonAsync(U("/api/payroll/assignments"), new { employeeId = employee.Id, structureId = structure.Id, @base = 10_000m, fromDate = "2025-01-01" }, Ct);
        var loan = (await (await admin.PostAsJsonAsync(U("/api/payroll/loans"), new
        {
            companyId = company.Id, employeeId = employee.Id, kind = "Loan", component = "Salary advance", principal = 2_000m, instalment = 1_000m, startMonth = "2026-11-01",
        }, Ct)).Content.ReadFromJsonAsync<EmployeeLoanDto>(Ct))!;
        (await admin.PostAsync(U($"/api/payroll/loans/{loan.Id}/submit"), null, Ct)).EnsureSuccessStatusCode();
        var slip = (await (await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"), new { employeeId = employee.Id, periodStart = "2026-11-01", periodEnd = "2026-11-30" }, Ct))
            .Content.ReadFromJsonAsync<SalarySlipDto>(Ct))!;

        var forLoan = (await admin.GetFromJsonAsync<List<RelatedDocument>>(U($"/api/documents/{loan.Id}/related?companyId={company.Id}"), Ct))!;
        Assert.Equal(slip.Id, Assert.Single(forLoan, d => d.Type == "SalarySlip").Id);
        var forSlip = (await admin.GetFromJsonAsync<List<RelatedDocument>>(U($"/api/documents/{slip.Id}/related?companyId={company.Id}"), Ct))!;
        Assert.Equal((loan.Id, loan.Number), (Assert.Single(forSlip, d => d.Type == "EmployeeLoan").Id!.Value, Assert.Single(forSlip).Number));
    }
}
