using System.Net;
using System.Net.Http.Json;
using Gebeta.Core.Contracts;
using Gebeta.Payroll.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>A full monthly payroll through the API: company, employees, structure, assignments, slips.</summary>
public class PayrollFlowTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    private async Task<(HttpClient Admin, CompanyDto Company, SalaryStructureDto Structure)> SetUpAsync(string abbreviation, string tin)
    {
        var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = $"Payroll Test {abbreviation}", abbreviation, legalForm = "PrivateLimitedCompany", tin }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var structure = await admin.PostAsJsonAsync(U("/api/payroll/structures"), new { companyId = company!.Id, name = "Standard Staff" }, Ct);
        Assert.Equal(HttpStatusCode.Created, structure.StatusCode);
        return (admin, company, (await structure.Content.ReadFromJsonAsync<SalaryStructureDto>(Ct))!);
    }

    private static async Task<EmployeeDto> HireAsync(HttpClient admin, Guid companyId, string first, string father, string joined, decimal basic, Guid structureId)
    {
        var employee = await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId, firstName = first, fatherName = father, grandfatherName = "Test", gender = "Female", dateOfJoining = joined }, Ct))
            .Content.ReadFromJsonAsync<EmployeeDto>(Ct);
        var assign = await admin.PostAsJsonAsync(U("/api/payroll/assignments"),
            new { employeeId = employee!.Id, structureId, @base = basic, fromDate = joined }, Ct);
        Assert.Equal(HttpStatusCode.Created, assign.StatusCode);
        return employee;
    }

    [Fact]
    public async Task A_monthly_payroll_from_employee_to_submitted_slip()
    {
        var (admin, company, structure) = await SetUpAsync("PFT", "0300000001");
        using var _ = admin;
        Assert.Equal(["BS", "IT", "PE", "PER"], structure.Components.Select(c => c.Abbreviation));

        var almaz = await HireAsync(admin, company.Id, "Almaz", "Tesfaye", "2025-01-01", 13_000m, structure.Id);
        Assert.Equal("EMP-00001", almaz.Number);
        Assert.Equal("Almaz Tesfaye Test", almaz.FullName);

        // Hidar 2019 E.C. as a Gregorian month (payroll period).
        var create = await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"),
            new { employeeId = almaz.Id, periodStart = "2026-11-01", periodEnd = "2026-11-30" }, Ct);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var slip = (await create.Content.ReadFromJsonAsync<SalarySlipDto>(Ct))!;

        Assert.Equal("SAL-2019-00001", slip.Number);       // Ethiopian fiscal year 2019
        Assert.Equal("Draft", slip.Status);
        Assert.Equal(30m, slip.PaymentDays);
        Assert.Equal(2_550m, slip.Lines.Single(l => l.Abbreviation == "IT").Amount);
        Assert.Equal(9_540m, slip.NetPay);
        Assert.Equal(1_430m, slip.EmployerContributions);
        Assert.Equal("Tikimt 2019", slip.EthiopianPeriod);  // 1 November 2026 = Tikimt 22, 2019

        var duplicate = await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"),
            new { employeeId = almaz.Id, periodStart = "2026-11-01", periodEnd = "2026-11-30" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var submitted = await (await admin.PostAsync(U($"/api/payroll/salary-slips/{slip.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<SalarySlipDto>(Ct);
        Assert.Equal("Submitted", submitted!.Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(U($"/api/payroll/salary-slips/{slip.Id}/submit"), null, Ct)).StatusCode);

        var list = await admin.GetFromJsonAsync<List<SalarySlipDto>>(U($"/api/payroll/salary-slips?companyId={company.Id}&from=2026-11-01&to=2026-11-30"), Ct);
        Assert.Equal([slip.Id], list!.Select(s => s.Id));
    }

    [Fact]
    public async Task A_cancelled_slip_can_be_remade_and_numbers_continue()
    {
        var (admin, company, structure) = await SetUpAsync("PFC", "0300000002");
        using var _ = admin;
        var dawit = await HireAsync(admin, company.Id, "Dawit", "Haile", "2025-01-01", 2_200m, structure.Id);
        var body = new { employeeId = dawit.Id, periodStart = "2026-11-01", periodEnd = "2026-11-30" };

        var first = await (await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"), body, Ct)).Content.ReadFromJsonAsync<SalarySlipDto>(Ct);
        await admin.PostAsync(U($"/api/payroll/salary-slips/{first!.Id}/submit"), null, Ct);
        var cancelled = await (await admin.PostAsync(U($"/api/payroll/salary-slips/{first.Id}/cancel"), null, Ct)).Content.ReadFromJsonAsync<SalarySlipDto>(Ct);
        Assert.Equal("Cancelled", cancelled!.Status);

        var second = await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"), body, Ct);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var remade = await second.Content.ReadFromJsonAsync<SalarySlipDto>(Ct);
        Assert.Equal("SAL-2019-00002", remade!.Number);
        Assert.Equal(2_016m, remade.NetPay);
    }

    [Fact]
    public async Task Someone_who_joined_mid_month_is_paid_for_the_days_employed()
    {
        var (admin, company, structure) = await SetUpAsync("PFM", "0300000003");
        using var _ = admin;
        var hirut = await HireAsync(admin, company.Id, "Hirut", "Bekele", "2026-11-16", 9_000m, structure.Id);

        var slip = await (await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"),
            new { employeeId = hirut.Id, periodStart = "2026-11-01", periodEnd = "2026-11-30" }, Ct)).Content.ReadFromJsonAsync<SalarySlipDto>(Ct);

        Assert.Equal(15m, slip!.PaymentDays);
        Assert.Equal(4_500m, slip.GrossPay);
    }

    [Fact]
    public async Task A_raise_applies_from_its_date_and_slips_need_an_assignment()
    {
        var (admin, company, structure) = await SetUpAsync("PFR", "0300000004");
        using var _ = admin;
        var abebe = await HireAsync(admin, company.Id, "Abebe", "Kebede", "2025-01-01", 12_000m, structure.Id);
        await admin.PostAsJsonAsync(U("/api/payroll/assignments"), new { employeeId = abebe.Id, structureId = structure.Id, @base = 13_000m, fromDate = "2026-11-01" }, Ct);

        var october = await (await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"),
            new { employeeId = abebe.Id, periodStart = "2026-10-01", periodEnd = "2026-10-31" }, Ct)).Content.ReadFromJsonAsync<SalarySlipDto>(Ct);
        var november = await (await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"),
            new { employeeId = abebe.Id, periodStart = "2026-11-01", periodEnd = "2026-11-30" }, Ct)).Content.ReadFromJsonAsync<SalarySlipDto>(Ct);

        Assert.Equal(12_000m, october!.Base);
        Assert.Equal(13_000m, november!.Base);

        var noAssignment = await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = company.Id, firstName = "New", fatherName = "Hire", gender = "Male", dateOfJoining = "2026-01-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct);
        var refused = await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"),
            new { employeeId = noAssignment!.Id, periodStart = "2026-11-01", periodEnd = "2026-11-30" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task Payroll_officers_cannot_see_other_companies_payroll()
    {
        var (admin, company, structure) = await SetUpAsync("PFX", "0300000005");
        using var _ = admin;
        var employee = await HireAsync(admin, company.Id, "Sara", "Mulu", "2025-01-01", 5_000m, structure.Id);
        using var officer = factory.ClientFor("officer-elsewhere");
        var me = await officer.GetFromJsonAsync<MeDto>(U("/api/me"), Ct);
        var role = (await admin.GetFromJsonAsync<List<RoleDto>>(U("/api/core/roles"), Ct))!.Single(r => r.Name == "Payroll Officer");
        await admin.PutAsync(U($"/api/core/users/{me!.Id}/roles/{role.Id}"), null, Ct);

        var slip = await officer.PostAsJsonAsync(U("/api/payroll/salary-slips"),
            new { employeeId = employee.Id, periodStart = "2026-11-01", periodEnd = "2026-11-30" }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, slip.StatusCode);
    }
}
