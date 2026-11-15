using System.Net;
using System.Net.Http.Json;
using Gebeta.Core.Contracts;
using Gebeta.Core.Presentation;

namespace Gebeta.Api.Tests;

public class ApiTests(GebetaApiFactory factory) : IClassFixture<GebetaApiFactory>
{
    private static readonly Uri Companies = new("/api/core/companies", UriKind.Relative);
    private static readonly Uri Me = new("/api/me", UriKind.Relative);
    private static readonly Uri Roles = new("/api/core/roles", UriKind.Relative);
    private static readonly Uri Users = new("/api/core/users", UriKind.Relative);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_answer_without_signing_in(string path)
    {
        using var client = factory.ClientFor(null);

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/modules")]
    [InlineData("/api/me")]
    [InlineData("/api/core/companies")]
    public async Task Everything_else_needs_a_signed_in_user(string path)
    {
        using var client = factory.ClientFor(null);

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Modules_endpoint_lists_the_installed_modules()
    {
        using var client = factory.ClientFor("modules-reader");

        var body = await client.GetFromJsonAsync<ModulesResponse>(new Uri("/api/modules", UriKind.Relative), Ct);

        Assert.Equal("core", body!.Modules[0].Id);
    }

    [Fact]
    public async Task First_sign_in_creates_the_user_and_bootstrap_admins_get_system_manager()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        using var newcomer = factory.ClientFor("newcomer");

        var adminMe = await admin.GetFromJsonAsync<MeDto>(Me, Ct);
        var newcomerMe = await newcomer.GetFromJsonAsync<MeDto>(Me, Ct);

        Assert.Contains("System Manager", adminMe!.Roles);
        Assert.Contains("*", adminMe.Permissions);
        Assert.Equal("newcomer@example.et", newcomerMe!.Email);
        Assert.Empty(newcomerMe.Roles);
        Assert.Empty(newcomerMe.Permissions);
        Assert.Equal("en", newcomerMe.Language);
        Assert.Equal("Both", newcomerMe.Calendar);
    }

    [Fact]
    public async Task A_user_can_choose_amharic_and_the_ethiopian_calendar()
    {
        using var client = factory.ClientFor("almaz");

        var response = await client.PutAsJsonAsync(new Uri("/api/me/preferences", UriKind.Relative),
            new { language = "am", calendar = "Ethiopian" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeDto>(Ct);
        Assert.Equal("am", me!.Language);
        Assert.Equal("Ethiopian", me.Calendar);

        var invalid = await client.PutAsJsonAsync(new Uri("/api/me/preferences", UriKind.Relative), new { language = "fr", calendar = "Both" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task System_roles_are_seeded()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);

        var roles = await admin.GetFromJsonAsync<List<RoleDto>>(Roles, Ct);

        Assert.Contains(roles!, r => r.Name == "System Manager" && r.IsSystem && r.Permissions.SequenceEqual(["*"]));
        Assert.Contains(roles!, r => r.Name == "Core Administrator" && r.Permissions.SequenceEqual(["core.*"]));
        Assert.Contains(roles!, r => r.Name == "Core Viewer");
    }

    [Fact]
    public async Task A_user_without_permission_is_forbidden_until_a_role_is_assigned()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        using var clerk = factory.ClientFor("clerk");
        var clerkMe = await clerk.GetFromJsonAsync<MeDto>(Me, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.GetAsync(Companies, Ct)).StatusCode);

        var viewer = (await admin.GetFromJsonAsync<List<RoleDto>>(Roles, Ct))!.Single(r => r.Name == "Core Viewer");
        var assign = await admin.PutAsync(new Uri($"/api/core/users/{clerkMe!.Id}/roles/{viewer.Id}", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await clerk.GetAsync(Companies, Ct)).StatusCode);
        var create = await clerk.PostAsJsonAsync(Companies, new { name = "Clerk Co", abbreviation = "CLK", legalForm = "ShareCompany", tin = "0011223344" }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        var users = await admin.GetFromJsonAsync<List<UserDto>>(Users, Ct);
        Assert.Contains(users!, u => u.Username == "clerk" && u.Roles.SequenceEqual(["Core Viewer"]));
    }

    [Fact]
    public async Task An_admin_cannot_remove_their_own_system_manager_role()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var me = await admin.GetFromJsonAsync<MeDto>(Me, Ct);
        var systemManager = (await admin.GetFromJsonAsync<List<RoleDto>>(Roles, Ct))!.Single(r => r.Name == "System Manager");

        var response = await admin.DeleteAsync(new Uri($"/api/core/users/{me!.Id}/roles/{systemManager.Id}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_custom_role_needs_valid_permissions()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);

        var ok = await admin.PostAsJsonAsync(Roles, new { name = "Company Clerk", permissions = new[] { "core.companies.read", "core.companies.create" } }, Ct);
        var bad = await admin.PostAsJsonAsync(Roles, new { name = "Broken", permissions = new[] { "companies" } }, Ct);
        var duplicate = await admin.PostAsJsonAsync(Roles, new { name = "Company Clerk", permissions = Array.Empty<string>() }, Ct);

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task A_company_can_be_created_read_and_listed()
    {
        using var client = factory.ClientFor(GebetaApiFactory.AdminUser);

        var create = await client.PostAsJsonAsync(Companies, new
        {
            name = "Ethiopian Sugar Industry Group",
            abbreviation = "ESIG",
            legalForm = "PublicEnterprise",
            tin = "0000123456",
        }, Ct);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<CompanyDto>(Ct);
        Assert.Equal("ETB", created!.DefaultCurrency);
        Assert.Equal("Ethiopian", created.FiscalYearType);

        var read = await client.GetFromJsonAsync<CompanyDto>(create.Headers.Location!, Ct);
        Assert.Equal(created, read);

        var list = await client.GetFromJsonAsync<List<CompanyDto>>(Companies, Ct);
        Assert.Contains(list!, c => c.Id == created.Id);
    }

    [Fact]
    public async Task Invalid_and_duplicate_companies_get_problem_details()
    {
        using var client = factory.ClientFor(GebetaApiFactory.AdminUser);

        var invalid = await client.PostAsJsonAsync(Companies, new { name = "X", abbreviation = "XY", legalForm = "ShareCompany", tin = "123" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var company = new { name = "Abay Systems", abbreviation = "ABAY", legalForm = "PrivateLimitedCompany", tin = "0099887766" };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(Companies, company, Ct)).StatusCode);
        var duplicate = await client.PostAsJsonAsync(Companies, company, Ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("\"code\":\"conflict\"", await duplicate.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_company_is_not_found()
    {
        using var client = factory.ClientFor(GebetaApiFactory.AdminUser);

        var response = await client.GetAsync(new Uri($"/api/core/companies/{Guid.NewGuid()}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_user_sees_only_the_companies_assigned_to_them()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        using var accountant = factory.ClientFor("accountant");
        var accountantMe = await accountant.GetFromJsonAsync<MeDto>(Me, Ct);
        var viewer = (await admin.GetFromJsonAsync<List<RoleDto>>(Roles, Ct))!.Single(r => r.Name == "Core Viewer");
        await admin.PutAsync(new Uri($"/api/core/users/{accountantMe!.Id}/roles/{viewer.Id}", UriKind.Relative), null, Ct);

        var wonji = await (await admin.PostAsJsonAsync(Companies, new { name = "Wonji Sugar Factory", abbreviation = "WSF", legalForm = "PublicEnterprise", tin = "0100000001" }, Ct))
            .Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var metehara = await (await admin.PostAsJsonAsync(Companies, new { name = "Metehara Sugar Factory", abbreviation = "MSF", legalForm = "PublicEnterprise", tin = "0100000002" }, Ct))
            .Content.ReadFromJsonAsync<CompanyDto>(Ct);

        // No access yet: the list is empty and a company is reported as not found.
        Assert.DoesNotContain(await accountant.GetFromJsonAsync<List<CompanyDto>>(Companies, Ct) ?? [], c => c.Id == wonji!.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await accountant.GetAsync(new Uri($"/api/core/companies/{wonji!.Id}", UriKind.Relative), Ct)).StatusCode);

        var grant = await admin.PutAsync(new Uri($"/api/core/users/{accountantMe.Id}/companies/{wonji.Id}", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        var visible = await accountant.GetFromJsonAsync<List<CompanyDto>>(Companies, Ct);
        Assert.Equal([wonji.Id], visible!.Select(c => c.Id));
        Assert.Equal(HttpStatusCode.OK, (await accountant.GetAsync(new Uri($"/api/core/companies/{wonji.Id}", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await accountant.GetAsync(new Uri($"/api/core/companies/{metehara!.Id}", UriKind.Relative), Ct)).StatusCode);

        var me = await accountant.GetFromJsonAsync<MeDto>(Me, Ct);
        Assert.False(me!.AccessAllCompanies);
        Assert.Equal([wonji.Id], me.CompanyIds);

        await admin.DeleteAsync(new Uri($"/api/core/users/{accountantMe.Id}/companies/{wonji.Id}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, (await accountant.GetAsync(new Uri($"/api/core/companies/{wonji.Id}", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task System_managers_see_every_company()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);

        var me = await admin.GetFromJsonAsync<MeDto>(Me, Ct);

        Assert.True(me!.AccessAllCompanies);
    }

    [Fact]
    public async Task The_creator_of_a_company_gets_access_to_it()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        using var founder = factory.ClientFor("founder");
        var founderMe = await founder.GetFromJsonAsync<MeDto>(Me, Ct);
        var role = await (await admin.PostAsJsonAsync(Roles, new { name = "Company Founder", permissions = new[] { "core.companies.read", "core.companies.create" } }, Ct))
            .Content.ReadFromJsonAsync<RoleDto>(Ct);
        await admin.PutAsync(new Uri($"/api/core/users/{founderMe!.Id}/roles/{role!.Id}", UriKind.Relative), null, Ct);

        var created = await (await founder.PostAsJsonAsync(Companies, new { name = "Founder Trading PLC", abbreviation = "FTP", legalForm = "PrivateLimitedCompany", tin = "0200000001" }, Ct))
            .Content.ReadFromJsonAsync<CompanyDto>(Ct);

        var visible = await founder.GetFromJsonAsync<List<CompanyDto>>(Companies, Ct);
        Assert.Equal([created!.Id], visible!.Select(c => c.Id));
    }

    [Fact]
    public async Task The_default_company_must_be_accessible()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        using var outsider = factory.ClientFor("outsider");
        var company = await (await admin.PostAsJsonAsync(Companies, new { name = "Finchaa Sugar Factory", abbreviation = "FSF", legalForm = "PublicEnterprise", tin = "0100000003" }, Ct))
            .Content.ReadFromJsonAsync<CompanyDto>(Ct);

        var response = await outsider.PutAsJsonAsync(new Uri("/api/me/preferences", UriKind.Relative),
            new { language = "en", calendar = "Both", defaultCompanyId = company!.Id }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_company_configures_its_naming_series_with_ethiopian_fiscal_years()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(Companies, new { name = "Tendaho Sugar Factory", abbreviation = "TSF", legalForm = "PublicEnterprise", tin = "0100000004" }, Ct))
            .Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var uri = new Uri($"/api/core/companies/{company!.Id}/naming-series", UriKind.Relative);

        var set = await admin.PutAsJsonAsync(uri, new { documentType = "Sales Invoice", pattern = "INV-.ABBR.-.FY.-.#####" }, Ct);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var dto = await set.Content.ReadFromJsonAsync<NamingSeriesDto>(Ct);
        Assert.StartsWith("INV-TSF-", dto!.Example, StringComparison.Ordinal);
        Assert.EndsWith("-00001", dto.Example, StringComparison.Ordinal);

        var changed = await admin.PutAsJsonAsync(uri, new { documentType = "Sales Invoice", pattern = "SI-.YYYY.-.####" }, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var list = await admin.GetFromJsonAsync<List<NamingSeriesDto>>(uri, Ct);
        Assert.Equal("SI-.YYYY.-.####", Assert.Single(list!).Pattern);

        var invalid = await admin.PutAsJsonAsync(uri, new { documentType = "Receipt", pattern = "REC-.YYYY." }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task The_holiday_calendar_has_national_holidays_and_announced_ones()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var uri = new Uri("/api/core/holidays/2019", UriKind.Relative);

        var before = await admin.GetFromJsonAsync<List<HolidayDto>>(uri, Ct);
        Assert.Contains(before!, h => h.Name.StartsWith("Enkutatash", StringComparison.Ordinal) && h.Date == new DateOnly(2026, 9, 11) && h.EthiopianDate == "01/01/2019");
        Assert.Contains(before!, h => h.Name.StartsWith("Fasika", StringComparison.Ordinal) && h.Kind == "Movable");

        var add = await admin.PostAsJsonAsync(new Uri("/api/core/holidays", UriKind.Relative),
            new { date = "2027-03-10", name = "Eid al-Fitr", amharicName = "ኢድ አልፈጥር" }, Ct);
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        var duplicate = await admin.PostAsJsonAsync(new Uri("/api/core/holidays", UriKind.Relative), new { date = "2027-03-10", name = "Eid al-Fitr" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var after = await admin.GetFromJsonAsync<List<HolidayDto>>(uri, Ct);
        var eid = Assert.Single(after!, h => h.Name == "Eid al-Fitr");
        Assert.Equal("Announced", eid.Kind);
        Assert.Equal("ኢድ አልፈጥር", eid.AmharicName);
    }

    [Fact]
    public async Task Holidays_need_the_read_permission()
    {
        using var stranger = factory.ClientFor("stranger");

        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync(new Uri("/api/core/holidays/2019", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task The_payroll_calculator_uses_the_ethiopian_standard_structure()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);

        var response = await admin.PostAsJsonAsync(new Uri("/api/payroll/calculator", UriKind.Relative),
            new { @base = 13000, payrollDate = "2026-11-30" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var slip = await response.Content.ReadFromJsonAsync<Gebeta.Payroll.Contracts.SalaryCalculationDto>(Ct);
        Assert.Equal(9540m, slip!.NetPay);
        Assert.Equal(1430m, slip.EmployerContributions);
        Assert.Equal("ET-EMP-2025", slip.TaxTable);

        var bad = await admin.PostAsJsonAsync(new Uri("/api/payroll/calculator", UriKind.Relative), new
        {
            @base = 1000,
            payrollDate = "2026-11-30",
            components = new[] { new { name = "Loop", abbreviation = "A", type = "Earning", formula = "A + 1" } },
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Payroll_is_listed_as_an_installed_module()
    {
        using var client = factory.ClientFor("module-checker");

        var body = await client.GetFromJsonAsync<ModulesResponse>(new Uri("/api/modules", UriKind.Relative), Ct);

        Assert.Contains(body!.Modules, m => m.Id == "payroll" && m.Enhances.Contains("hr"));
    }

    [Fact]
    public async Task OpenApi_document_is_published()
    {
        using var client = factory.ClientFor(null);

        var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/api/core/companies", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
