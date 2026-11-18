using System.Net;
using System.Net.Http.Json;
using Gebeta.Core.Contracts;
using Gebeta.Hr.Contracts;
using Gebeta.Payroll.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>Employee self-service: a user linked to their employee record sees their own payslips and leave, and nobody else's.</summary>
public class SelfServiceTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task An_employee_sees_only_their_own_submitted_payslips_and_applies_for_leave_for_themselves()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Self Service PLC", abbreviation = "SSV", legalForm = "PrivateLimitedCompany", tin = "0800000320" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var structure = await (await admin.PostAsJsonAsync(U("/api/payroll/structures"), new { companyId = company!.Id, name = "Standard" }, Ct))
            .Content.ReadFromJsonAsync<SalaryStructureDto>(Ct);

        async Task<EmployeeDto> Hire(string first, string father)
        {
            var e = await (await admin.PostAsJsonAsync(U("/api/core/employees"),
                new { companyId = company.Id, firstName = first, fatherName = father, gender = "Female", dateOfJoining = "2024-01-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct);
            await admin.PostAsJsonAsync(U("/api/payroll/assignments"), new { employeeId = e!.Id, structureId = structure!.Id, @base = 13_000m, fromDate = "2024-01-01" }, Ct);
            return e;
        }

        async Task<SalarySlipDto> Slip(EmployeeDto e, string start, string end, bool submit)
        {
            var slip = (await (await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"), new { employeeId = e.Id, periodStart = start, periodEnd = end }, Ct))
                .Content.ReadFromJsonAsync<SalarySlipDto>(Ct))!;
            return submit ? (await (await admin.PostAsync(U($"/api/payroll/salary-slips/{slip.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<SalarySlipDto>(Ct))! : slip;
        }

        var almaz = await Hire("Almaz", "Tesfaye");
        var kebede = await Hire("Kebede", "Alemu");
        var september = await Slip(almaz, "2026-09-01", "2026-09-30", submit: true);
        await Slip(almaz, "2026-10-01", "2026-10-31", submit: false);            // a draft: payroll's until submitted
        var kebedes = await Slip(kebede, "2026-09-01", "2026-09-30", submit: true);

        // Almaz signs in, and an administrator gives her the Employee role and links her to her record.
        using var me = factory.ClientFor("almaz.self");
        Assert.Equal(HttpStatusCode.OK, (await me.GetAsync(U("/api/me"), Ct)).StatusCode);
        var users = (await admin.GetFromJsonAsync<List<UserDto>>(U("/api/core/users"), Ct))!;
        var user = users.Single(u => u.Username == "almaz.self");
        var employeeRole = (await admin.GetFromJsonAsync<List<RoleDto>>(U("/api/core/roles"), Ct))!.Single(r => r.Name == "Employee");
        await admin.PutAsync(U($"/api/core/users/{user.Id}/roles/{employeeRole.Id}"), null, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, (await me.GetAsync(U("/api/payroll/my/salary-slips"), Ct)).StatusCode); // not linked yet
        var linked = await admin.PutAsJsonAsync(U($"/api/core/users/{user.Id}/employee"), new { employeeId = almaz.Id }, Ct);
        Assert.Equal(almaz.Id, (await linked.Content.ReadFromJsonAsync<UserDto>(Ct))!.EmployeeId);
        Assert.Contains(company.Id, (await me.GetFromJsonAsync<MeDto>(U("/api/me"), Ct))!.CompanyIds); // linking gives her the company

        var mine = (await me.GetFromJsonAsync<List<SalarySlipDto>>(U("/api/payroll/my/salary-slips"), Ct))!;
        Assert.Equal(september.Id, Assert.Single(mine).Id);
        Assert.Equal(9_540m, (await me.GetFromJsonAsync<SalarySlipDto>(U($"/api/payroll/my/salary-slips/{september.Id}"), Ct))!.NetPay);
        Assert.Equal(HttpStatusCode.NotFound, (await me.GetAsync(U($"/api/payroll/my/salary-slips/{kebedes.Id}"), Ct)).StatusCode); // a colleague's
        Assert.Equal(HttpStatusCode.Forbidden, (await me.GetAsync(U($"/api/payroll/salary-slips?companyId={company.Id}"), Ct)).StatusCode); // payroll's list

        // Leave: she applies for herself; the application is hers whatever she sends.
        await admin.PostAsJsonAsync(U("/api/hr/leave-allocations"), new { companyId = company.Id, fiscalYear = 2019 }, Ct);
        var applied = await me.PostAsJsonAsync(U("/api/hr/my/leave-applications"),
            new { type = "Annual", fromDate = "2026-11-24", toDate = "2026-11-25", reason = "Family", employeeId = kebede.Id }, Ct);
        Assert.Equal(HttpStatusCode.Created, applied.StatusCode);
        Assert.Equal(almaz.Id, (await applied.Content.ReadFromJsonAsync<LeaveApplicationDto>(Ct))!.EmployeeId);
        var leave = (await me.GetFromJsonAsync<MyLeaveDto>(U("/api/hr/my/leave"), Ct))!;
        Assert.Equal("Draft", Assert.Single(leave.Applications).Status);
        Assert.True(leave.Balances.Single(b => b.Type == "Annual").Allocated > 0m);
        Assert.Equal(HttpStatusCode.Forbidden, (await me.GetAsync(U($"/api/hr/leave-applications?companyId={company.Id}&from=2026-01-01&to=2026-12-31"), Ct)).StatusCode);

        // One user per employee.
        using var other = factory.ClientFor("almaz.twin");
        await other.GetAsync(U("/api/me"), Ct);
        var twin = (await admin.GetFromJsonAsync<List<UserDto>>(U("/api/core/users"), Ct))!.Single(u => u.Username == "almaz.twin");
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(U($"/api/core/users/{twin.Id}/employee"), new { employeeId = almaz.Id }, Ct)).StatusCode);
    }
}
