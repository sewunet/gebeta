using System.Net;
using System.Net.Http.Json;
using Gebeta.Core.Contracts;
using Gebeta.Payroll.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>A retroactive increment through the API, paid in the next payroll.</summary>
public class BackPaymentFlowTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task An_increment_approved_late_is_paid_in_the_november_payroll()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Back Pay Test PLC", abbreviation = "BPT", legalForm = "PrivateLimitedCompany", tin = "0400000001" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var structure = await (await admin.PostAsJsonAsync(U("/api/payroll/structures"), new { companyId = company!.Id, name = "Staff" }, Ct))
            .Content.ReadFromJsonAsync<SalaryStructureDto>(Ct);
        var abebe = await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = company.Id, firstName = "Abebe", fatherName = "Kebede", gender = "Male", dateOfJoining = "2025-07-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct);
        await admin.PostAsJsonAsync(U("/api/payroll/assignments"), new { employeeId = abebe!.Id, structureId = structure!.Id, @base = 12_000m, fromDate = "2025-07-01" }, Ct);

        // August–October paid at the old salary.
        foreach (var (start, end) in new[] { ("2026-08-01", "2026-08-31"), ("2026-09-01", "2026-09-30"), ("2026-10-01", "2026-10-31") })
        {
            var slip = await (await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"), new { employeeId = abebe.Id, periodStart = start, periodEnd = end }, Ct))
                .Content.ReadFromJsonAsync<SalarySlipDto>(Ct);
            await admin.PostAsync(U($"/api/payroll/salary-slips/{slip!.Id}/submit"), null, Ct);
        }

        // The increment to 13,000 from August is approved in November.
        var revised = await (await admin.PostAsJsonAsync(U("/api/payroll/assignments"),
            new { employeeId = abebe.Id, structureId = structure.Id, @base = 13_000m, fromDate = "2026-08-01" }, Ct)).Content.ReadFromJsonAsync<SalaryStructureAssignmentDto>(Ct);

        var create = await admin.PostAsJsonAsync(U("/api/payroll/back-payments"), new
        {
            employeeId = abebe.Id,
            assignmentId = revised!.Id,
            effectiveFrom = "2026-08-01",
            effectiveTo = "2026-10-31",
            payrollDate = "2026-11-25",
            reason = "Salary increment approved late",
            reference = "HR/INC/2026/017",
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var backPayment = (await create.Content.ReadFromJsonAsync<SalaryBackPaymentDto>(Ct))!;
        Assert.Equal("SBP-2019-00001", backPayment.Number);
        Assert.Equal(3, backPayment.Months.Count);
        Assert.Equal(3_000m, backPayment.TotalGrossArrears);
        Assert.Equal(900m, backPayment.TotalAdditionalTax);
        Assert.Equal(210m, backPayment.TotalOtherDeductions);
        Assert.Equal(1_890m, backPayment.NetArrears);
        Assert.Equal("Hamle 2018", backPayment.Months[0].EthiopianMonth); // 1 August 2026 = Hamle 25, 2018

        var submitted = await (await admin.PostAsync(U($"/api/payroll/back-payments/{backPayment.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<SalaryBackPaymentDto>(Ct);
        Assert.Equal("Submitted", submitted!.Status);
        Assert.Equal(["BP_ARR", "BP_TAX", "BP_DED", "BP_EMP"], submitted.Entries.Select(e => e.Abbreviation));

        // November: the new salary plus the arrears, with no extra tax in November.
        var november = await (await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"),
            new { employeeId = abebe.Id, periodStart = "2026-11-01", periodEnd = "2026-11-30" }, Ct)).Content.ReadFromJsonAsync<SalarySlipDto>(Ct);
        Assert.Equal(2_550m, november!.Lines.Single(l => l.Abbreviation == "IT").Amount);
        Assert.Equal(3_000m, november.Lines.Single(l => l.Abbreviation == "BP_ARR").Amount);
        Assert.Equal(9_540m + 1_890m, november.NetPay);
        Assert.Equal(13_000m, november.TaxableEarnings);

        // The back payment cannot be cancelled while its arrears are in a slip.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(U($"/api/payroll/back-payments/{backPayment.Id}/cancel"), null, Ct)).StatusCode);

        // Paying the same months again gives nothing to pay.
        var again = await (await admin.PostAsJsonAsync(U("/api/payroll/back-payments"), new
        {
            employeeId = abebe.Id, assignmentId = revised.Id, effectiveFrom = "2026-08-01", effectiveTo = "2026-10-31",
            payrollDate = "2026-12-25", reason = "Duplicate by mistake",
        }, Ct)).Content.ReadFromJsonAsync<SalaryBackPaymentDto>(Ct);
        Assert.Equal(0m, again!.NetArrears);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(U($"/api/payroll/back-payments/{again.Id}/submit"), null, Ct)).StatusCode);

        // Cancelling the November slip releases the arrears; then the back payment can be cancelled.
        await admin.PostAsync(U($"/api/payroll/salary-slips/{november.Id}/submit"), null, Ct);
        await admin.PostAsync(U($"/api/payroll/salary-slips/{november.Id}/cancel"), null, Ct);
        var cancelled = await admin.PostAsync(U($"/api/payroll/back-payments/{backPayment.Id}/cancel"), null, Ct);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);

        var remade = await (await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"),
            new { employeeId = abebe.Id, periodStart = "2026-11-01", periodEnd = "2026-11-30" }, Ct)).Content.ReadFromJsonAsync<SalarySlipDto>(Ct);
        Assert.Equal(9_540m, remade!.NetPay);
        Assert.DoesNotContain(remade.Lines, l => l.Abbreviation.StartsWith("BP_", StringComparison.Ordinal));
    }
}
