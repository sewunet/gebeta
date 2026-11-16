using System.Net;
using System.Net.Http.Json;
using Gebeta.Core.Contracts;
using Gebeta.Payroll.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>A company's monthly payroll in one run, then the tax, pension and bank reports.</summary>
public class PayrollRunTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task A_payroll_run_pays_everyone_and_produces_the_monthly_reports()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Run Test PLC", abbreviation = "RUN", legalForm = "PrivateLimitedCompany", tin = "0500000001" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var structure = await (await admin.PostAsJsonAsync(U("/api/payroll/structures"), new { companyId = company!.Id, name = "Staff" }, Ct))
            .Content.ReadFromJsonAsync<SalaryStructureDto>(Ct);

        async Task<EmployeeDto> Hire(string first, string father, string tin, decimal? basic, string? bank, string? account)
        {
            var e = await (await admin.PostAsJsonAsync(U("/api/core/employees"),
                new { companyId = company.Id, firstName = first, fatherName = father, gender = "Female", dateOfJoining = "2025-01-01", tin }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct);
            if (basic is { } b)
            {
                await admin.PostAsJsonAsync(U("/api/payroll/assignments"), new { employeeId = e!.Id, structureId = structure!.Id, @base = b, fromDate = "2025-01-01" }, Ct);
            }

            if (bank is not null)
            {
                var profile = await admin.PutAsJsonAsync(U($"/api/payroll/employees/{e!.Id}/profile"),
                    new { paymentMethod = "BankTransfer", bankName = bank, bankAccountNumber = account, pensionNumber = $"PEN-{first}" }, Ct);
                Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
            }

            return e!;
        }

        await Hire("Almaz", "Tesfaye", "0600000001", 13_000m, "Commercial Bank of Ethiopia", "1000123456789");
        await Hire("Dawit", "Haile", "0600000002", 2_200m, "Awash Bank", "0132 0045 6789");
        await Hire("Selam", "Girma", "0600000003", 8_500m, null, null);          // no profile: listed as cash with a warning
        await Hire("Yonas", "Bekele", "0600000004", null, null, null);           // no salary assignment: an issue

        var badBank = await admin.PutAsJsonAsync(U($"/api/payroll/employees/{Guid.NewGuid()}/profile"), new { paymentMethod = "BankTransfer" }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, badBank.StatusCode);

        var run = await (await admin.PostAsJsonAsync(U("/api/payroll/runs"), new { companyId = company.Id, periodStart = "2026-11-01", periodEnd = "2026-11-30" }, Ct))
            .Content.ReadFromJsonAsync<PayrollRunDto>(Ct);
        Assert.Equal("PR-2019-0001", run!.Number);

        var generated = await (await admin.PostAsync(U($"/api/payroll/runs/{run.Id}/generate"), null, Ct)).Content.ReadFromJsonAsync<PayrollRunDto>(Ct);
        Assert.Equal(3, generated!.SlipCount);
        var issue = Assert.Single(generated.Issues);
        Assert.Contains("Yonas", issue.EmployeeName, StringComparison.Ordinal);
        Assert.Equal(9_540m + 2_016m + 6_630m, generated.TotalNet);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync(U($"/api/payroll/runs/{run.Id}/generate"), null, Ct)).StatusCode);

        var submitted = await (await admin.PostAsync(U($"/api/payroll/runs/{run.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<PayrollRunDto>(Ct);
        Assert.Equal("Submitted", submitted!.Status);

        var query = $"companyId={company.Id}&periodStart=2026-11-01&periodEnd=2026-11-30";
        var tax = await admin.GetFromJsonAsync<IncomeTaxDeclarationDto>(U($"/api/payroll/reports/income-tax?{query}"), Ct);
        Assert.Equal(3, tax!.Employees);
        Assert.Equal("0500000001", tax.CompanyTin);
        Assert.Equal(2_550m + 30m + 1_275m, tax.TotalTaxWithheld);
        Assert.Equal("0600000001", tax.Rows[0].Tin);
        Assert.Equal("Tikimt 2019", tax.EthiopianMonth);

        var pension = await admin.GetFromJsonAsync<PensionScheduleDto>(U($"/api/payroll/reports/pension?{query}"), Ct);
        Assert.Equal(910m + 154m + 595m, pension!.TotalEmployee);
        Assert.Equal(1_430m + 242m + 935m, pension.TotalEmployer);
        Assert.Equal("PEN-Almaz", pension.Rows[0].PensionNumber);

        var bank = await admin.GetFromJsonAsync<BankTransferDto>(U($"/api/payroll/reports/bank-transfer?{query}"), Ct);
        Assert.Equal(2, bank!.BankTransfers.Count);
        Assert.Equal("013200456789", bank.BankTransfers.Single(r => r.EmployeeName.StartsWith("Dawit", StringComparison.Ordinal)).AccountNumber);
        Assert.Equal(6_630m, Assert.Single(bank.Cash).NetPay);
        Assert.Contains("Selam", Assert.Single(bank.Issues), StringComparison.Ordinal);

        var csv = await admin.GetAsync(U($"/api/payroll/reports/income-tax?{query}&format=csv"), Ct);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        var text = await csv.Content.ReadAsStringAsync(Ct);
        Assert.StartsWith("Employee No,Employee Name,TIN", text.TrimStart('﻿'), StringComparison.Ordinal);
        Assert.Contains("13000.00", text, StringComparison.Ordinal);
    }
}
