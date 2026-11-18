using System.Net.Http.Json;
using Gebeta.Core.Contracts;
using Gebeta.Payroll.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>Payroll checks before submit (docs/requirements/sap-features.md #1): one employee per problem, and one with none.</summary>
public class PayrollChecksTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task A_run_lists_every_employee_who_fails_a_check_and_leaves_a_clean_employee_out()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Checks PLC", Ct);
        var structure = (await (await admin.PostAsJsonAsync(U("/api/payroll/structures"), new { companyId = company.Id, name = "Standard" }, Ct))
            .Content.ReadFromJsonAsync<SalaryStructureDto>(Ct))!;

        async Task<EmployeeDto> Hire(string first, decimal basic, bool tin = true, string? method = "BankTransfer", string? account = null, string? pension = "P-1")
        {
            var e = (await (await admin.PostAsJsonAsync(U("/api/core/employees"), new
            {
                companyId = company.Id, firstName = first, fatherName = "Test", gender = "Male", dateOfJoining = "2024-01-01", tin = tin ? TestData.Tin() : null,
            }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct))!;
            await admin.PostAsJsonAsync(U("/api/payroll/assignments"), new { employeeId = e.Id, structureId = structure.Id, @base = basic, fromDate = "2024-01-01" }, Ct);
            if (method is not null)
            {
                (await admin.PutAsJsonAsync(U($"/api/payroll/employees/{e.Id}/profile"), new
                {
                    paymentMethod = method, bankName = method == "Cash" ? null : "CBE", bankAccountNumber = method == "Cash" ? null : account ?? $"1000{Random.Shared.Next(100_000, 999_999)}",
                    pensionNumber = pension,
                }, Ct)).EnsureSuccessStatusCode();
            }

            return e;
        }

        async Task Loan(EmployeeDto e, decimal instalment)
        {
            var draft = (await (await admin.PostAsJsonAsync(U("/api/payroll/loans"), new
            {
                companyId = company.Id, employeeId = e.Id, kind = "StandingDeduction", component = "Court order", abbreviation = "COURT", instalment, startMonth = "2026-11-01",
            }, Ct)).Content.ReadFromJsonAsync<EmployeeLoanDto>(Ct))!;
            (await admin.PostAsync(U($"/api/payroll/loans/{draft.Id}/submit"), null, Ct)).EnsureSuccessStatusCode();
        }

        var clean = await Hire("Abebe", 10_000m);
        var low = await Hire("Bekele", 6_000m);
        await Loan(low, 3_500m);
        var negative = await Hire("Chala", 3_000m);
        await Loan(negative, 5_000m);
        var shared1 = await Hire("Dawit", 8_000m, account: "2000000002");
        var shared2 = await Hire("Elias", 8_000m, account: "2000000002");
        var bare = await Hire("Fikru", 8_000m, tin: false, method: null);
        var cash = await Hire("Genet", 8_000m, method: "Cash");

        var run = (await (await admin.PostAsJsonAsync(U("/api/payroll/runs"), new { companyId = company.Id, periodStart = "2026-11-01", periodEnd = "2026-11-30" }, Ct))
            .Content.ReadFromJsonAsync<PayrollRunDto>(Ct))!;
        (await admin.PostAsync(U($"/api/payroll/runs/{run.Id}/generate"), null, Ct)).EnsureSuccessStatusCode();

        var checks = (await admin.GetFromJsonAsync<PayrollChecksDto>(U($"/api/payroll/runs/{run.Id}/checks"), Ct))!;
        string[] Of(EmployeeDto e) => checks.Rows.Where(r => r.EmployeeId == e.Id).Select(r => r.Check).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(7, checks.SlipsChecked);
        Assert.Equal(["Net pay below zero"], Of(negative));
        Assert.Equal("Error", checks.Rows.Single(r => r.EmployeeId == negative.Id).Severity);
        Assert.Empty(Of(clean));
        Assert.Empty(Of(cash)); // paid in cash: no bank account needed
        Assert.Equal(["Net pay below the minimum share of gross"], Of(low));
        Assert.Matches(@"Other deductions: Court order \(LOAN-\d{4}-\d{5}\) 3,500\.00\.", checks.Rows.Single(r => r.EmployeeId == low.Id).Message); // the cause, by number
        Assert.Equal(["Bank account shared with another employee"], Of(shared1));
        Assert.Equal(["Bank account shared with another employee"], Of(shared2));
        Assert.Equal(["No TIN", "No bank account", "No pension number"], Of(bare));
        Assert.Equal((1, 6, 5), (checks.Errors, checks.Warnings, checks.FlaggedEmployees));
        Assert.Equal("Net pay below zero", checks.Counts[0].Check); // errors first
        Assert.Equal(2, checks.Counts.Single(c => c.Check == "Bank account shared with another employee").Employees);

        var csv = await (await admin.GetAsync(U($"/api/payroll/runs/{run.Id}/checks?format=csv"), Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Warning,No bank account", csv, StringComparison.Ordinal);
    }
}
