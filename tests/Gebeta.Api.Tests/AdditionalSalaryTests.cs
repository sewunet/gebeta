using System.Net;
using System.Net.Http.Json;
using Gebeta.Core.Contracts;
using Gebeta.Payroll.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>Additional salary: overtime, allowances and deductions for many employees, taken into the month's slip and taxed with it.</summary>
public class AdditionalSalaryTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task Overtime_and_a_deduction_go_into_the_months_calculation_and_cannot_be_cancelled_once_in_a_slip()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Overtime PLC", abbreviation = "OVT", legalForm = "PrivateLimitedCompany", tin = "0800000330" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var structure = await (await admin.PostAsJsonAsync(U("/api/payroll/structures"), new { companyId = company!.Id, name = "Standard" }, Ct))
            .Content.ReadFromJsonAsync<SalaryStructureDto>(Ct);

        async Task<EmployeeDto> Hire(string first, string father)
        {
            var e = await (await admin.PostAsJsonAsync(U("/api/core/employees"),
                new { companyId = company.Id, firstName = first, fatherName = father, gender = "Female", dateOfJoining = "2024-01-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct);
            await admin.PostAsJsonAsync(U("/api/payroll/assignments"), new { employeeId = e!.Id, structureId = structure!.Id, @base = 10_000m, fromDate = "2024-01-01" }, Ct);
            return e;
        }

        async Task<AdditionalSalaryDto> Additional(string component, string abbreviation, string type, bool taxable, string month, params (EmployeeDto Employee, decimal Amount)[] lines)
        {
            var created = await admin.PostAsJsonAsync(U("/api/payroll/additional-salaries"), new
            {
                companyId = company.Id, payrollDate = month, component, abbreviation, type, isTaxable = taxable,
                lines = lines.Select(l => new { employeeId = l.Employee.Id, amount = l.Amount, note = (string?)null }).ToArray(), remark = (string?)null,
            }, Ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var draft = (await created.Content.ReadFromJsonAsync<AdditionalSalaryDto>(Ct))!;
            return (await (await admin.PostAsync(U($"/api/payroll/additional-salaries/{draft.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<AdditionalSalaryDto>(Ct))!;
        }

        var almaz = await Hire("Almaz", "Tesfaye");
        var kebede = await Hire("Kebede", "Alemu");
        var overtime = await Additional("Overtime", "OT", "Earning", true, "2026-10-15", (almaz, 1_500m), (kebede, 500m));
        Assert.Equal(("Submitted", 2_000m), (overtime.Status, overtime.Total));
        await Additional("Cooperative", "COOP", "Deduction", false, "2026-10-20", (almaz, 300m));

        // Almaz's October: 10,000 + 1,500 overtime = 11,500; tax on 11,500 = 30% − 1,350 = 2,100; pension 7% of basic 700; cooperative 300.
        var slip = (await (await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"), new { employeeId = almaz.Id, periodStart = "2026-10-01", periodEnd = "2026-10-31" }, Ct))
            .Content.ReadFromJsonAsync<SalarySlipDto>(Ct))!;
        decimal Line(string abbreviation) => slip.Lines.Where(l => l.Abbreviation == abbreviation).Sum(l => l.Amount);
        Assert.Equal((1_500m, 300m, 2_100m, 700m), (Line("OT"), Line("COOP"), Line("IT"), Line("PE")));
        Assert.Equal((11_500m, 8_400m), (slip.GrossPay, slip.NetPay));

        // In a slip now: the overtime cannot be cancelled any more.
        var refused = await admin.PostAsync(U($"/api/payroll/additional-salaries/{overtime.Id}/cancel"), null, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("already in salary slips", await refused.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(1, (await admin.GetFromJsonAsync<AdditionalSalaryDto>(U($"/api/payroll/additional-salaries/{overtime.Id}"), Ct))!.InSlips);

        // Cancelled before any slip: Kebede's November has no allowance.
        var allowance = await Additional("Field Allowance", "FIELD", "Earning", true, "2026-11-10", (kebede, 800m));
        var cancelled = (await (await admin.PostAsync(U($"/api/payroll/additional-salaries/{allowance.Id}/cancel"), null, Ct)).Content.ReadFromJsonAsync<AdditionalSalaryDto>(Ct))!;
        Assert.Equal("Cancelled", cancelled.Status);
        var november = (await (await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"), new { employeeId = kebede.Id, periodStart = "2026-11-01", periodEnd = "2026-11-30" }, Ct))
            .Content.ReadFromJsonAsync<SalarySlipDto>(Ct))!;
        Assert.Equal(10_000m, november.GrossPay);

        var listed = (await admin.GetFromJsonAsync<List<AdditionalSalaryDto>>(U($"/api/payroll/additional-salaries?companyId={company.Id}&from=2026-10-01&to=2026-11-30"), Ct))!;
        Assert.Equal(3, listed.Count);
    }

    [Fact]
    public async Task An_additional_salary_refuses_an_employee_twice_and_another_companys_employee()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Overtime Checks PLC", abbreviation = "OVC", legalForm = "PrivateLimitedCompany", tin = "0800000331" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var other = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Elsewhere PLC", abbreviation = "ELW", legalForm = "PrivateLimitedCompany", tin = "0800000332" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var mine = await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = company!.Id, firstName = "Hana", fatherName = "Girma", gender = "Female", dateOfJoining = "2024-01-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct);
        var theirs = await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = other!.Id, firstName = "Dawit", fatherName = "Haile", gender = "Male", dateOfJoining = "2024-01-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct);

        async Task<HttpResponseMessage> Create(params Guid[] employees) => await admin.PostAsJsonAsync(U("/api/payroll/additional-salaries"), new
        {
            companyId = company.Id, payrollDate = "2026-10-15", component = "Overtime", abbreviation = "OT", type = "Earning", isTaxable = true,
            lines = employees.Select(e => new { employeeId = e, amount = 100m, note = (string?)null }).ToArray(), remark = (string?)null,
        }, Ct);

        Assert.Contains("appears once", await (await Create(mine!.Id, mine.Id)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Contains("not an employee of this company", await (await Create(theirs!.Id)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
