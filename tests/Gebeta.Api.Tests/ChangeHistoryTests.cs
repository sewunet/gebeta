using System.Net;
using System.Net.Http.Json;
using Gebeta.Accounting.Contracts;
using Gebeta.Application;
using Gebeta.Core.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>Change history (docs/requirements/sap-features.md #4): who changed which field, when, from what to what.</summary>
public class ChangeHistoryTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task An_employees_history_shows_their_own_changes_and_their_bank_account_changes_from_payroll()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("History PLC", Ct);
        var employee = (await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = company.Id, firstName = "Almaz", fatherName = "Tesfaye", gender = "Female", dateOfJoining = "2024-01-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct))!;
        (await admin.PutAsJsonAsync(U($"/api/core/employees/{employee.Id}"),
            new { firstName = "Almaz", fatherName = "Tesfaye", grandfatherName = (string?)null, gender = "Female", dateOfJoining = "2024-02-01", tin = (string?)null }, Ct))
            .EnsureSuccessStatusCode();

        async Task Bank(string account) => (await admin.PutAsJsonAsync(U($"/api/payroll/employees/{employee.Id}/profile"),
            new { paymentMethod = "BankTransfer", bankName = "CBE", bankAccountNumber = account, pensionNumber = "P-1" }, Ct)).EnsureSuccessStatusCode();
        await Bank("1000111222");
        await Bank("1000999888"); // the classic fraud signal: the account changed

        var history = (await admin.GetFromJsonAsync<List<ChangeRecord>>(U($"/api/history/{employee.Id}"), Ct))!;
        Assert.All(history, h => Assert.Equal("admin", h.By));
        Assert.Contains(history, h => h is { EntityType: "Employee", Action: "Created" });
        Assert.Contains(history, h => h is { EntityType: "Employee", Field: "DateOfJoining", OldValue: "2024-01-01", NewValue: "2024-02-01" });
        Assert.Contains(history, h => h is { EntityType: "EmployeePayrollProfile", Field: "BankAccountNumber", OldValue: "1000111222", NewValue: "1000999888" });
        Assert.DoesNotContain(history, h => h.Field is "ModifiedAt" or "ModifiedBy"); // bookkeeping is not news
        Assert.True(history.SequenceEqual(history.OrderByDescending(h => h.At).ThenBy(h => h.Field, StringComparer.Ordinal)));
    }

    [Fact]
    public async Task A_document_records_its_submit_and_the_history_needs_the_permission()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("History Journal PLC", Ct);
        var journal = (await (await admin.PostAsJsonAsync(U("/api/accounting/journals"), new
        {
            companyId = company.Id, postingDate = "2026-11-12", remark = "Rent",
            lines = new[]
            {
                new { accountCode = "6200", debit = 500m, credit = 0m, description = (string?)null },
                new { accountCode = "1120", debit = 0m, credit = 500m, description = (string?)null },
            },
        }, Ct)).Content.ReadFromJsonAsync<JournalEntryDto>(Ct))!;
        (await admin.PostAsync(U($"/api/accounting/journals/{journal.Id}/submit"), null, Ct)).EnsureSuccessStatusCode();

        var history = (await admin.GetFromJsonAsync<List<ChangeRecord>>(U($"/api/history/{journal.Id}"), Ct))!;
        Assert.Contains(history, h => h is { EntityType: "JournalEntry", Action: "Created" });
        Assert.Contains(history, h => h is { Field: "DocStatus", OldValue: "Draft", NewValue: "Submitted" });

        using var stranger = factory.ClientFor("history.stranger");
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync(U($"/api/history/{journal.Id}"), Ct)).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<List<ChangeRecord>>(U($"/api/history/{Guid.NewGuid()}"), Ct))!);
    }
}
