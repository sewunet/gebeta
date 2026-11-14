using System.Net;
using System.Net.Http.Json;
using Gebeta.Core.Contracts;
using Gebeta.Core.Presentation;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gebeta.Api.Tests;

public class ApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_answer(string path)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Modules_endpoint_lists_the_installed_modules()
    {
        using var client = factory.CreateClient();

        var body = await client.GetFromJsonAsync<ModulesResponse>(new Uri("/api/modules", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        var core = Assert.Single(body.Modules);
        Assert.Equal("core", core.Id);
    }

    [Fact]
    public async Task A_company_can_be_created_read_and_listed()
    {
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var create = await client.PostAsJsonAsync(new Uri("/api/core/companies", UriKind.Relative), new
        {
            name = "Ethiopian Sugar Industry Group",
            abbreviation = "ESIG",
            legalForm = "PublicEnterprise",
            tin = "0000123456",
        }, ct);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<CompanyDto>(ct);
        Assert.NotNull(created);
        Assert.Equal("ETB", created.DefaultCurrency);
        Assert.Equal("Ethiopian", created.FiscalYearType);

        var read = await client.GetFromJsonAsync<CompanyDto>(create.Headers.Location!, ct);
        Assert.Equal(created, read);

        var list = await client.GetFromJsonAsync<List<CompanyDto>>(new Uri("/api/core/companies", UriKind.Relative), ct);
        Assert.Contains(list!, c => c.Id == created.Id);
    }

    [Fact]
    public async Task Invalid_and_duplicate_companies_get_problem_details()
    {
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;
        var uri = new Uri("/api/core/companies", UriKind.Relative);

        var invalid = await client.PostAsJsonAsync(uri, new { name = "X", abbreviation = "XY", legalForm = "ShareCompany", tin = "123" }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var company = new { name = "Abay Systems", abbreviation = "ABAY", legalForm = "PrivateLimitedCompany", tin = "0099887766" };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(uri, company, ct)).StatusCode);
        var duplicate = await client.PostAsJsonAsync(uri, company, ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("\"code\":\"conflict\"", await duplicate.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_company_is_not_found()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri($"/api/core/companies/{Guid.NewGuid()}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_document_is_published()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/api/modules", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }
}
