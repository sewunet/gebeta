using System.Net;
using System.Net.Http.Json;
using Gebeta.Core.Contracts;
using Gebeta.Core.Presentation;
using Gebeta.Hr.Contracts;
using Gebeta.Payroll.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>HR plugged in next to Payroll: leave and attendance change the payment days of the salary slip.</summary>
public class HrPayrollTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task With_hr_installed_payroll_takes_payment_days_from_hr()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var modules = await admin.GetFromJsonAsync<ModulesResponse>(U("/api/modules"), Ct);
        var binding = Assert.Single(modules!.Capabilities, c => c.Capability == "IPaymentDaysProvider");
        Assert.Equal("hr", binding.ProvidedBy);
        Assert.False(binding.IsFallback);
    }

    [Fact]
    public async Task Unpaid_leave_and_absences_reduce_pay_and_annual_leave_does_not()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "HR Test PLC", abbreviation = "HRT", legalForm = "PrivateLimitedCompany", tin = "0700000001" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var structure = await (await admin.PostAsJsonAsync(U("/api/payroll/structures"), new { companyId = company!.Id, name = "Staff" }, Ct))
            .Content.ReadFromJsonAsync<SalaryStructureDto>(Ct);
        var almaz = await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = company.Id, firstName = "Almaz", fatherName = "Tesfaye", gender = "Female", dateOfJoining = "2025-01-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct);
        await admin.PostAsJsonAsync(U("/api/payroll/assignments"), new { employeeId = almaz!.Id, structureId = structure!.Id, @base = 13_000m, fromDate = "2025-01-01" }, Ct);

        // Annual leave for FY 2019: one completed year → 16 working days.
        var allocation = await (await admin.PostAsJsonAsync(U("/api/hr/leave-allocations"), new { companyId = company.Id, fiscalYear = 2019 }, Ct))
            .Content.ReadFromJsonAsync<AllocationResultDto>(Ct);
        Assert.Equal(1, allocation!.Allocated);
        Assert.Equal(16m, allocation.TotalDays);

        async Task<LeaveApplicationDto> ApplyAndApprove(string type, string from, string to)
        {
            var applied = await admin.PostAsJsonAsync(U("/api/hr/leave-applications"), new { employeeId = almaz.Id, type, fromDate = from, toDate = to }, Ct);
            Assert.Equal(HttpStatusCode.Created, applied.StatusCode);
            var leave = await applied.Content.ReadFromJsonAsync<LeaveApplicationDto>(Ct);
            return (await (await admin.PostAsync(U($"/api/hr/leave-applications/{leave!.Id}/approve"), null, Ct)).Content.ReadFromJsonAsync<LeaveApplicationDto>(Ct))!;
        }

        var unpaid = await ApplyAndApprove("Unpaid", "2026-11-09", "2026-11-10");     // 2 days without pay
        var annual = await ApplyAndApprove("Annual", "2026-11-16", "2026-11-20");     // 5 working days, paid
        Assert.Equal("LV-2019-00001", unpaid.Number);
        Assert.Equal(5m, annual.Days);
        Assert.Equal("Submitted", annual.Status);

        await admin.PutAsJsonAsync(U("/api/hr/attendance"), new { employeeId = almaz.Id, date = "2026-11-12", status = "Absent" }, Ct);
        await admin.PutAsJsonAsync(U("/api/hr/attendance"), new { employeeId = almaz.Id, date = "2026-11-13", status = "HalfDay" }, Ct);
        await admin.PutAsJsonAsync(U("/api/hr/attendance"), new { employeeId = almaz.Id, date = "2026-11-17", status = "Absent" }, Ct); // on annual leave: not counted

        var balance = await admin.GetFromJsonAsync<List<LeaveBalanceDto>>(U($"/api/hr/employees/{almaz.Id}/leave-balance"), Ct);
        Assert.Equal(11m, balance!.Single(b => b.Type == "Annual").Balance);

        var overlap = await admin.PostAsJsonAsync(U("/api/hr/leave-applications"), new { employeeId = almaz.Id, type = "Annual", fromDate = "2026-11-18", toDate = "2026-11-19" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);

        var slip = await (await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"),
            new { employeeId = almaz.Id, periodStart = "2026-11-01", periodEnd = "2026-11-30" }, Ct)).Content.ReadFromJsonAsync<SalarySlipDto>(Ct);

        // 30 − 2 unpaid − 1 absent − 0.5 half day = 26.5 days.
        Assert.Equal(26.5m, slip!.PaymentDays);
        Assert.Equal(11_483.33m, slip.GrossPay);
    }
}
