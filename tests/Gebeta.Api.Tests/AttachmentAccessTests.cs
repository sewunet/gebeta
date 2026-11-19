using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Gebeta.Application;
using Gebeta.Core.Contracts;
using Gebeta.Payroll.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>A file is as private as the record it belongs to: attachments permission alone opens nothing (docs/requirements/sap-features.md #5).</summary>
public class AttachmentAccessTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n% court order\n");

    private static MultipartFormDataContent Upload(Guid companyId, Guid entityId)
    {
        var file = new ByteArrayContent(Pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { new StringContent(companyId.ToString()), "companyId" }, { new StringContent(entityId.ToString()), "entityId" }, { file, "file", "order.pdf" } };
    }

    /// <summary>A user in the company holding only these permissions.</summary>
    private async Task<HttpClient> UserWithAsync(HttpClient admin, string username, Guid companyId, params string[] permissions)
    {
        var client = factory.ClientFor(username);
        var me = (await client.GetFromJsonAsync<MeDto>(U("/api/me"), Ct))!;
        var role = (await (await admin.PostAsJsonAsync(U("/api/core/roles"), new { name = $"Role of {username}", permissions }, Ct)).Content.ReadFromJsonAsync<RoleDto>(Ct))!;
        (await admin.PutAsync(U($"/api/core/users/{me.Id}/roles/{role.Id}"), null, Ct)).EnsureSuccessStatusCode();
        (await admin.PutAsync(U($"/api/core/users/{me.Id}/companies/{companyId}"), null, Ct)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<List<AttachmentDto>> ListAsync(HttpClient client, Guid companyId, Guid entityId) =>
        (await client.GetFromJsonAsync<List<AttachmentDto>>(U($"/api/attachments?companyId={companyId}&entityId={entityId}"), Ct))!;

    [Fact]
    public async Task Attachments_permission_without_the_records_permission_opens_nothing_on_an_employee()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Attachment Access PLC", Ct);
        var employee = (await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = company.Id, firstName = "Selam", fatherName = "Test", gender = "Female", dateOfJoining = "2024-01-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct))!;
        var file = (await (await admin.PostAsync(U("/api/attachments"), Upload(company.Id, employee.Id), Ct)).Content.ReadFromJsonAsync<AttachmentDto>(Ct))!;

        // Attachments permission only: every way in is refused as if the record were not there, and the file stays.
        using var blind = await UserWithAsync(admin, "attachment.blind", company.Id, "core.attachments.read", "core.attachments.manage");
        Assert.Equal(HttpStatusCode.NotFound, (await blind.GetAsync(U($"/api/attachments?companyId={company.Id}&entityId={employee.Id}"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await blind.GetAsync(U($"/api/attachments/{file.Id}/content"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await blind.PostAsync(U("/api/attachments"), Upload(company.Id, employee.Id), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await blind.DeleteAsync(U($"/api/attachments/{file.Id}"), Ct)).StatusCode);
        Assert.Single(await ListAsync(admin, company.Id, employee.Id));

        // May read the employee: lists and downloads, but may not add or remove (that needs the employee's manage permission).
        using var reader = await UserWithAsync(admin, "attachment.reader", company.Id, "core.attachments.read", "core.attachments.manage", "core.employees.read");
        Assert.Equal(file.Id, Assert.Single(await ListAsync(reader, company.Id, employee.Id)).Id);
        Assert.Equal(Pdf, await (await reader.GetAsync(U($"/api/attachments/{file.Id}/content"), Ct)).Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal(HttpStatusCode.NotFound, (await reader.PostAsync(U("/api/attachments"), Upload(company.Id, employee.Id), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.DeleteAsync(U($"/api/attachments/{file.Id}"), Ct)).StatusCode);
        Assert.Single(await ListAsync(admin, company.Id, employee.Id));

        // May manage the employee: adds and removes.
        using var manager = await UserWithAsync(admin, "attachment.manager", company.Id,
            "core.attachments.read", "core.attachments.manage", "core.employees.read", "core.employees.manage");
        var added = await manager.PostAsync(U("/api/attachments"), Upload(company.Id, employee.Id), Ct);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Equal(2, (await ListAsync(manager, company.Id, employee.Id)).Count);
        (await manager.DeleteAsync(U($"/api/attachments/{file.Id}"), Ct)).EnsureSuccessStatusCode();
        Assert.Single(await ListAsync(admin, company.Id, employee.Id));
    }

    [Fact]
    public async Task A_payroll_record_is_guarded_by_the_payroll_permission_and_an_unknown_record_is_refused()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Attachment Payroll PLC", Ct);
        var structure = (await (await admin.PostAsJsonAsync(U("/api/payroll/structures"), new { companyId = company.Id, name = "Staff" }, Ct)).Content.ReadFromJsonAsync<SalaryStructureDto>(Ct))!;
        var employee = (await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = company.Id, firstName = "Hana", fatherName = "Test", gender = "Female", dateOfJoining = "2025-01-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct))!;
        await admin.PostAsJsonAsync(U("/api/payroll/assignments"), new { employeeId = employee.Id, structureId = structure.Id, @base = 13_000m, fromDate = "2025-01-01" }, Ct);
        var slip = (await (await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"), new { employeeId = employee.Id, periodStart = "2026-11-01", periodEnd = "2026-11-30" }, Ct))
            .Content.ReadFromJsonAsync<SalarySlipDto>(Ct))!;
        var file = (await (await admin.PostAsync(U("/api/attachments"), Upload(company.Id, slip.Id), Ct)).Content.ReadFromJsonAsync<AttachmentDto>(Ct))!;

        // Reads the employee but not salary slips: the slip's file is closed to them.
        using var hr = await UserWithAsync(admin, "attachment.hr", company.Id, "core.attachments.read", "core.attachments.manage", "core.employees.read", "core.employees.manage");
        Assert.Equal(HttpStatusCode.NotFound, (await hr.GetAsync(U($"/api/attachments?companyId={company.Id}&entityId={slip.Id}"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await hr.GetAsync(U($"/api/attachments/{file.Id}/content"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await hr.PostAsync(U("/api/attachments"), Upload(company.Id, slip.Id), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await hr.DeleteAsync(U($"/api/attachments/{file.Id}"), Ct)).StatusCode);

        using var payroll = await UserWithAsync(admin, "attachment.payroll", company.Id,
            "core.attachments.read", "core.attachments.manage", PayrollPermissions.SlipsRead, PayrollPermissions.SlipsCreate);
        Assert.Equal(file.Id, Assert.Single(await ListAsync(payroll, company.Id, slip.Id)).Id);
        Assert.Equal(HttpStatusCode.OK, (await payroll.GetAsync(U($"/api/attachments/{file.Id}/content"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await payroll.PostAsync(U("/api/attachments"), Upload(company.Id, slip.Id), Ct)).StatusCode);

        // A record no module knows (even for the administrator), and a record of one company filed under another company, are refused.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync(U("/api/attachments"), Upload(company.Id, Guid.NewGuid()), Ct)).StatusCode);
        var other = await admin.NewCompanyAsync("Attachment Payroll Elsewhere PLC", Ct);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync(U("/api/attachments"), Upload(other.Id, slip.Id), Ct)).StatusCode);
    }
}
