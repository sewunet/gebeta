using System.Net;
using System.Net.Http.Json;
using Gebeta.Application;
using Gebeta.Buying.Contracts;
using Gebeta.Core.Application.Alerts;
using Gebeta.Core.Contracts;
using Gebeta.Accounting.Contracts;
using Gebeta.Payroll.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Api.Tests;

/// <summary>Alerts from saved conditions (docs/requirements/sap-features.md #9): rules, the daily run, no repeats, who is told, who may set them up.</summary>
public class AlertTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    private const string Steered = "test.controllable";

    private static async Task<AlertRuleDto> CreateRuleAsync(HttpClient client, Guid companyId, string condition, string[]? users = null, string[]? roles = null, int? threshold = null, string? name = null)
    {
        var response = await client.PostAsJsonAsync(U("/api/core/alerts/rules"), new { companyId, conditionId = condition, name, threshold, userNames = users ?? [], roleNames = roles ?? [] }, Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<AlertRuleDto>(Ct))!;
    }

    private static async Task<AlertRunDto> RunAsync(HttpClient client, Guid ruleId, string? asOf = null)
    {
        var response = await client.PostAsync(U($"/api/core/alerts/rules/{ruleId}/run" + (asOf is null ? string.Empty : $"?asOf={asOf}")), null, Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<AlertRunDto>(Ct))!;
    }

    private static async Task<IReadOnlyList<NotificationDto>> InboxAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<NotificationPageDto>(U("/api/notifications?pageSize=100"), Ct))!.Items;

    private static async Task<AlertRuleDto> RuleAsync(HttpClient client, Guid companyId, Guid id) =>
        (await client.GetFromJsonAsync<List<AlertRuleDto>>(U($"/api/core/alerts/rules?companyId={companyId}"), Ct))!.Single(r => r.Id == id);

    [Fact]
    public async Task A_rule_tells_each_item_once_and_tells_it_again_only_after_it_clears_and_comes_back()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Alert Once PLC", Ct);
        var (ann, _, _) = await factory.UserWithAsync(admin, "alert.once.ann", company.Id, "core.companies.read");
        using var annClient = ann;
        var rule = await CreateRuleAsync(admin, company.Id, Steered, users: ["alert.once.ann"], name: "Steered");
        var condition = ControllableAlertCondition.Instance;

        // First run: two items match, both are new.
        condition.Match(company.Id, "k1", "k2");
        Assert.Equal((2, 2, 0), Counts(await RunAsync(admin, rule.Id)));
        Assert.Equal(["Item k1", "Item k2"], (await InboxAsync(annClient)).Select(n => n.Title).Order(StringComparer.Ordinal));

        // Running again changes nothing: no second message for the same items.
        Assert.Equal((2, 0, 0), Counts(await RunAsync(admin, rule.Id)));
        Assert.Equal(2, (await InboxAsync(annClient)).Count);

        // A new item is told alone.
        condition.Match(company.Id, "k1", "k2", "k3");
        Assert.Equal((3, 1, 0), Counts(await RunAsync(admin, rule.Id)));
        Assert.Equal(3, (await InboxAsync(annClient)).Count);

        // k2 clears: nothing is told (messages already sent stay), and it is forgotten.
        condition.Match(company.Id, "k1", "k3");
        Assert.Equal((2, 0, 1), Counts(await RunAsync(admin, rule.Id)));
        Assert.Equal(3, (await InboxAsync(annClient)).Count);

        // k2 comes back: told again, as a fourth message.
        condition.Match(company.Id, "k1", "k2", "k3");
        Assert.Equal((3, 1, 0), Counts(await RunAsync(admin, rule.Id)));
        var inbox = await InboxAsync(annClient);
        Assert.Equal(4, inbox.Count);
        Assert.Equal("Item k2", inbox[0].Title); // newest first
        Assert.Equal("Steered", inbox[0].Body);   // the rule's name says which alert it came from

        var shown = await RuleAsync(admin, company.Id, rule.Id);
        Assert.Equal((3, 1, null, true), (shown.LastMatchCount, shown.LastNotifiedCount, shown.LastError, shown.ConditionAvailable));
        Assert.NotNull(shown.LastRunOn);
    }

    private static (int, int, int) Counts(AlertRunDto run) => (run.Matched, run.Notified, run.Cleared);

    [Fact]
    public async Task A_condition_that_fails_changes_nothing_so_its_items_are_not_told_again_when_it_recovers()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Alert Failing PLC", Ct);
        var (bea, _, _) = await factory.UserWithAsync(admin, "alert.failing.bea", company.Id, "core.companies.read");
        using var beaClient = bea;
        var rule = await CreateRuleAsync(admin, company.Id, Steered, users: ["alert.failing.bea"]);
        var condition = ControllableAlertCondition.Instance;

        condition.Match(company.Id, "k1");
        Assert.Equal((1, 1, 0), Counts(await RunAsync(admin, rule.Id)));

        condition.Fail(company.Id, true);
        var failed = await RunAsync(admin, rule.Id);
        Assert.Equal((0, 0, 0), Counts(failed));
        Assert.Contains("broken on purpose", failed.Error, StringComparison.Ordinal);
        Assert.Contains("broken on purpose", (await RuleAsync(admin, company.Id, rule.Id)).LastError, StringComparison.Ordinal);

        // Recovered: k1 is still remembered (a failure must not look like "everything cleared"), so it is not told again.
        condition.Fail(company.Id, false);
        Assert.Equal((1, 0, 0), Counts(await RunAsync(admin, rule.Id)));
        Assert.Single(await InboxAsync(beaClient));
        Assert.Null((await RuleAsync(admin, company.Id, rule.Id)).LastError);
    }

    [Fact]
    public async Task A_role_reaches_each_holder_once_who_may_work_in_the_company_and_read_what_the_alert_shows()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Alert Role PLC", Ct);
        var elsewhere = await admin.NewCompanyAsync("Alert Role Elsewhere PLC", Ct);
        var (one, _, role) = await factory.UserWithAsync(admin, "alert.role.one", company.Id, "core.companies.read");
        var (two, twoMe, _) = await factory.UserWithAsync(admin, "alert.role.two", company.Id, "core.companies.read");
        var (outside, outsideMe, _) = await factory.UserWithAsync(admin, "alert.role.outside", elsewhere.Id, "core.companies.read");
        var (blind, blindMe, _) = await factory.UserWithAsync(admin, "alert.role.blind", company.Id, "core.employees.read");
        using var oneClient = one;
        using var twoClient = two;
        using var outsideClient = outside;
        using var blindClient = blind;

        // Two and the outsider hold the first user's role as well as their own; the outsider does not work in this company.
        foreach (var me in new[] { twoMe, outsideMe })
        {
            (await admin.PutAsync(U($"/api/core/users/{me.Id}/roles/{role.Id}"), null, Ct)).EnsureSuccessStatusCode();
        }

        // Two is named and holds the role: told once, not twice.
        var rule = await CreateRuleAsync(admin, company.Id, Steered, users: ["alert.role.two"], roles: [role.Name]);
        ControllableAlertCondition.Instance.Match(company.Id, "k1");
        Assert.Equal((1, 1, 0), Counts(await RunAsync(admin, rule.Id)));
        Assert.Single(await InboxAsync(oneClient));
        Assert.Single(await InboxAsync(twoClient));
        Assert.Empty(await InboxAsync(outsideClient));

        // Someone who may not read what the alert shows cannot be named, directly or by role, and an unknown name is refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/core/alerts/rules"), new { companyId = company.Id, conditionId = Steered, userNames = new[] { "alert.role.blind" } }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/core/alerts/rules"), new { companyId = company.Id, conditionId = Steered, roleNames = new[] { $"Role of alert.role.blind" } }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/core/alerts/rules"), new { companyId = company.Id, conditionId = Steered, userNames = new[] { "alert.role.nobody" } }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/core/alerts/rules"), new { companyId = company.Id, conditionId = Steered, roleNames = new[] { "No such role" } }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/core/alerts/rules"), new { companyId = company.Id, conditionId = Steered }, Ct)).StatusCode); // nobody named
        Assert.Empty(await InboxAsync(blindClient));

        // A role that lost the permission after the rule was made is skipped when the rule runs (deny by default), and the rule says why.
        (await admin.PostAsJsonAsync(U("/api/core/roles"), new { name = "Alert Role Later", permissions = new[] { "core.companies.read" } }, Ct)).EnsureSuccessStatusCode();
        var later = (await (await admin.GetAsync(U("/api/core/roles"), Ct)).Content.ReadFromJsonAsync<List<RoleDto>>(Ct))!.Single(r => r.Name == "Alert Role Later");
        (await admin.PutAsync(U($"/api/core/users/{blindMe.Id}/roles/{later.Id}"), null, Ct)).EnsureSuccessStatusCode();
        var laterRule = await CreateRuleAsync(admin, company.Id, Steered, roles: ["Alert Role Later"]);
        (await admin.DeleteAsync(U($"/api/core/users/{blindMe.Id}/roles/{later.Id}"), Ct)).EnsureSuccessStatusCode();
        ControllableAlertCondition.Instance.Match(company.Id, "k2");
        var run = await RunAsync(admin, laterRule.Id);
        Assert.Equal((1, 0, 0), Counts(run));
        Assert.NotNull(run.Error);
        Assert.Empty(await InboxAsync(blindClient));
    }

    [Fact]
    public async Task More_than_ten_new_items_arrive_as_one_summary_per_person()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Alert Backlog PLC", Ct);
        var (cy, _, _) = await factory.UserWithAsync(admin, "alert.backlog.cy", company.Id, "core.companies.read");
        using var cyClient = cy;
        var rule = await CreateRuleAsync(admin, company.Id, Steered, users: ["alert.backlog.cy"], name: "Backlog");
        var condition = ControllableAlertCondition.Instance;

        // Twelve at once (more than the ten sent one by one): a single message, and all twelve are remembered.
        condition.Match(company.Id, Enumerable.Range(1, 12).Select(i => $"b{i:D2}").ToArray());
        Assert.Equal((12, 12, 0), Counts(await RunAsync(admin, rule.Id)));
        var inbox = await InboxAsync(cyClient);
        Assert.Equal("Backlog: 12 new items", Assert.Single(inbox).Title);
        Assert.Equal((12, 12, 0), (12, (await RuleAsync(admin, company.Id, rule.Id)).LastNotifiedCount, 0));
        Assert.Equal((12, 0, 0), Counts(await RunAsync(admin, rule.Id)));
        Assert.Single(await InboxAsync(cyClient));

        // Ten is still told one by one.
        condition.Match(company.Id, [.. Enumerable.Range(1, 12).Select(i => $"b{i:D2}"), .. Enumerable.Range(1, 10).Select(i => $"c{i:D2}")]);
        Assert.Equal((22, 10, 0), Counts(await RunAsync(admin, rule.Id)));
        Assert.Equal(11, (await InboxAsync(cyClient)).Count);
    }

    [Fact]
    public async Task Rules_are_switched_on_and_off_edited_run_by_the_schedule_once_a_day_and_deleted_with_what_they_remember()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Alert Switch PLC", Ct);
        var (di, _, _) = await factory.UserWithAsync(admin, "alert.switch.di", company.Id, "core.companies.read");
        using var diClient = di;
        var rule = await CreateRuleAsync(admin, company.Id, Steered, users: ["ALERT.switch.DI"]); // names are matched without regard to case
        Assert.Equal((Steered, true, null), (rule.Name, rule.IsEnabled, rule.Threshold)); // the name defaults to the condition's id
        Assert.Equal(["alert.switch.di"], rule.UserNames);
        ControllableAlertCondition.Instance.Match(company.Id, "k1");

        // Off: "run now" says to switch it on; the schedule leaves it alone.
        var off = await (await admin.PostAsync(U($"/api/core/alerts/rules/{rule.Id}/disable"), null, Ct)).Content.ReadFromJsonAsync<AlertRuleDto>(Ct);
        Assert.False(off!.IsEnabled);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(U($"/api/core/alerts/rules/{rule.Id}/run"), null, Ct)).StatusCode);

        // The schedule runs a rule once for each date: the same date again does nothing, the next date runs again.
        (await admin.PostAsync(U($"/api/core/alerts/rules/{rule.Id}/enable"), null, Ct)).EnsureSuccessStatusCode();
        var due = await ScheduledAsync(rule.Id, new DateOnly(2026, 11, 20));
        Assert.Equal((1, 1, false), (due.Matched, due.Notified, due.Skipped));
        Assert.True((await ScheduledAsync(rule.Id, new DateOnly(2026, 11, 20))).Skipped);
        Assert.Equal((1, 0, false), ((await ScheduledAsync(rule.Id, new DateOnly(2026, 11, 21))).Matched, 0, false));
        Assert.Single(await InboxAsync(diClient)); // the second date's run found it still there, and did not repeat it

        // Switched off and on again, it still remembers: nothing is repeated.
        await admin.PostAsync(U($"/api/core/alerts/rules/{rule.Id}/disable"), null, Ct);
        Assert.True((await ScheduledAsync(rule.Id, new DateOnly(2026, 11, 22))).Skipped);
        await admin.PostAsync(U($"/api/core/alerts/rules/{rule.Id}/enable"), null, Ct);
        Assert.Equal(0, (await RunAsync(admin, rule.Id)).Notified);

        // Edited: a new name; a condition without a threshold keeps none.
        var edited = await (await admin.PutAsJsonAsync(U($"/api/core/alerts/rules/{rule.Id}"), new { name = "Renamed", threshold = 5, userNames = new[] { "alert.switch.di" }, roleNames = Array.Empty<string>() }, Ct))
            .Content.ReadFromJsonAsync<AlertRuleDto>(Ct);
        Assert.Equal(("Renamed", (int?)null), (edited!.Name, edited.Threshold));

        // Deleted: gone from the list; running it is not found.
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync(U($"/api/core/alerts/rules/{rule.Id}"), Ct)).StatusCode);
        Assert.DoesNotContain((await admin.GetFromJsonAsync<List<AlertRuleDto>>(U($"/api/core/alerts/rules?companyId={company.Id}"), Ct))!, r => r.Id == rule.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync(U($"/api/core/alerts/rules/{rule.Id}/run"), null, Ct)).StatusCode);
    }

    private async Task<AlertRunDto> ScheduledAsync(Guid ruleId, DateOnly today)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<AlertRunner>().RunAsync(ruleId, today, manual: false, Ct)).Value;
    }

    [Fact]
    public async Task Setting_up_and_running_rules_needs_the_alerts_permission_the_company_and_the_permission_the_condition_reads()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Alert Access PLC", Ct);
        var other = await admin.NewCompanyAsync("Alert Access Other PLC", Ct);
        var (viewer, _, _) = await factory.UserWithAsync(admin, "alert.access.viewer", company.Id, "core.alerts.read", "core.companies.read");
        var (blind, _, _) = await factory.UserWithAsync(admin, "alert.access.blind", company.Id, "core.alerts.read", "core.alerts.manage"); // no core.companies.read
        var (manager, _, _) = await factory.UserWithAsync(admin, "alert.access.manager", company.Id, "core.alerts.read", "core.alerts.manage", "core.companies.read");
        var (nothing, _, _) = await factory.UserWithAsync(admin, "alert.access.nothing", company.Id, "core.companies.read");
        using var viewerClient = viewer;
        using var blindClient = blind;
        using var managerClient = manager;
        using var nothingClient = nothing;

        var rule = await CreateRuleAsync(admin, company.Id, Steered, users: ["alert.access.manager"]);

        // Reading: the viewer sees the conditions and the rule, but cannot change or run it. Without core.alerts.read there is nothing.
        Assert.Contains((await viewerClient.GetFromJsonAsync<List<AlertConditionDto>>(U("/api/core/alerts/conditions"), Ct))!, c => c.Id == Steered);
        Assert.Equal(rule.Id, Assert.Single((await viewerClient.GetFromJsonAsync<List<AlertRuleDto>>(U($"/api/core/alerts/rules?companyId={company.Id}"), Ct))!).Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewerClient.PostAsJsonAsync(U("/api/core/alerts/rules"), new { companyId = company.Id, conditionId = Steered, userNames = new[] { "alert.access.manager" } }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewerClient.PostAsync(U($"/api/core/alerts/rules/{rule.Id}/run"), null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewerClient.PostAsync(U($"/api/core/alerts/rules/{rule.Id}/disable"), null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewerClient.DeleteAsync(U($"/api/core/alerts/rules/{rule.Id}"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothingClient.GetAsync(U($"/api/core/alerts/rules?companyId={company.Id}"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.ClientFor(null).GetAsync(U("/api/core/alerts/conditions"), Ct)).StatusCode);

        // A manager who may not read what the condition shows does not see the condition or the rule, and cannot touch it: answered as not found.
        Assert.DoesNotContain((await blindClient.GetFromJsonAsync<List<AlertConditionDto>>(U("/api/core/alerts/conditions"), Ct))!, c => c.Id == Steered);
        Assert.Empty((await blindClient.GetFromJsonAsync<List<AlertRuleDto>>(U($"/api/core/alerts/rules?companyId={company.Id}"), Ct))!);
        Assert.Equal(HttpStatusCode.NotFound, (await blindClient.PostAsJsonAsync(U("/api/core/alerts/rules"), new { companyId = company.Id, conditionId = Steered, userNames = new[] { "alert.access.manager" } }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await blindClient.PostAsync(U($"/api/core/alerts/rules/{rule.Id}/run"), null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await blindClient.PostAsync(U($"/api/core/alerts/rules/{rule.Id}/disable"), null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await blindClient.DeleteAsync(U($"/api/core/alerts/rules/{rule.Id}"), Ct)).StatusCode);

        // A manager who may read it can do all of it, in their own company only.
        var made = await CreateRuleAsync(managerClient, company.Id, Steered, users: ["alert.access.manager"]);
        Assert.Equal(HttpStatusCode.OK, (await managerClient.PostAsync(U($"/api/core/alerts/rules/{made.Id}/run"), null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await managerClient.PostAsJsonAsync(U("/api/core/alerts/rules"), new { companyId = other.Id, conditionId = Steered, userNames = new[] { "alert.access.manager" } }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await managerClient.GetAsync(U($"/api/core/alerts/rules?companyId={other.Id}"), Ct)).StatusCode);
        var foreign = await CreateRuleAsync(admin, other.Id, Steered, users: ["alert.access.manager"]);
        Assert.Equal(HttpStatusCode.NotFound, (await managerClient.PostAsync(U($"/api/core/alerts/rules/{foreign.Id}/run"), null, Ct)).StatusCode);

        // An unknown condition is not found; a condition outside its threshold range is refused.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync(U("/api/core/alerts/rules"), new { companyId = company.Id, conditionId = "nobody.offers-this", userNames = new[] { "alert.access.manager" } }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync(U("/api/core/alerts/rules"), new { companyId = Guid.NewGuid(), conditionId = Steered, userNames = new[] { "alert.access.manager" } }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/core/alerts/rules"), new { companyId = company.Id, conditionId = "accounting.overdue-sales-invoices", threshold = 0, userNames = new[] { "admin" } }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Overdue_invoices_are_told_by_the_days_past_due_and_clear_when_paid()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Alert Overdue PLC", Ct);
        var (eve, _, _) = await factory.UserWithAsync(admin, "alert.overdue.eve", company.Id, "accounting.invoices.read");
        using var eveClient = eve;
        var customer = (await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = company.Id, name = "Overdue Customer", type = "Customer", tin = TestData.Tin() }, Ct)).Content.ReadFromJsonAsync<PartyDto>(Ct))!;
        var supplier = (await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = company.Id, name = "Overdue Supplier", type = "Supplier", tin = TestData.Tin() }, Ct)).Content.ReadFromJsonAsync<PartyDto>(Ct))!;

        async Task<InvoiceDto> Submitted(string kind, Guid party, string due, int rate, string account)
        {
            var draft = (await (await admin.PostAsJsonAsync(U("/api/accounting/invoices"), new
            {
                kind, companyId = company.Id, partyId = party, postingDate = "2026-01-05", dueDate = due, supplierInvoiceNumber = kind == "Purchase" ? "SUP-1" : null,
                lines = new object[] { new { description = "Goods", quantity = 1, rate, accountCode = account, isService = false, vatApplicable = false } },
            }, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct))!;
            return (await (await admin.PostAsync(U($"/api/accounting/invoices/{draft.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct))!;
        }

        // Sales: one due on 2026-02-01 (9 days before the 10th), one due on the 10th itself (not yet overdue), one not yet due. Purchase: due 2026-02-01.
        var late = await Submitted("Sales", customer.Id, "2026-02-01", 1_000, "4100");
        await Submitted("Sales", customer.Id, "2026-02-10", 1_000, "4100");
        await Submitted("Sales", customer.Id, "2026-03-01", 1_000, "4100");
        var owed = await Submitted("Purchase", supplier.Id, "2026-02-01", 2_000, "6900");

        // Threshold 10 days: the one 9 days late is not yet; 9 days: it is. Judged as of 2026-02-10.
        var sales = await CreateRuleAsync(admin, company.Id, "accounting.overdue-sales-invoices", users: ["alert.overdue.eve"], threshold: 10);
        Assert.Equal((0, 0, 0), Counts(await RunAsync(admin, sales.Id, "2026-02-10")));
        await admin.PutAsJsonAsync(U($"/api/core/alerts/rules/{sales.Id}"), new { name = "Late payers", threshold = 9, userNames = new[] { "alert.overdue.eve" }, roleNames = Array.Empty<string>() }, Ct);
        Assert.Equal((1, 1, 0), Counts(await RunAsync(admin, sales.Id, "2026-02-10")));

        var note = Assert.Single(await InboxAsync(eveClient));
        Assert.Equal(($"{late.Number}: Overdue Customer, 9 days overdue", "Invoice", late.Id, "Warning"), (note.Title, note.LinkKind, note.LinkId, note.Severity));

        // Tomorrow it is the same invoice, 10 days late (and the second one is now 1 day late: below the threshold of 9): still one, not repeated.
        Assert.Equal((1, 0, 0), Counts(await RunAsync(admin, sales.Id, "2026-02-11")));

        // Paid in full: it clears. (A sales payment of the whole 1,000: no VAT, no withholding.)
        (await admin.PostAsJsonAsync(U("/api/accounting/payments"), new { invoiceId = late.Id, amount = 1_000m, postingDate = "2026-02-11", reference = "CBE" }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal((0, 0, 1), Counts(await RunAsync(admin, sales.Id, "2026-02-11")));

        // Purchases are a condition of their own: the supplier invoice is 9 days late, and only it.
        var purchases = await CreateRuleAsync(admin, company.Id, "accounting.overdue-purchase-invoices", users: ["alert.overdue.eve"], threshold: 1);
        Assert.Equal((1, 1, 0), Counts(await RunAsync(admin, purchases.Id, "2026-02-10")));
        Assert.Equal(owed.Id, (await InboxAsync(eveClient))[0].LinkId);
    }

    [Fact]
    public async Task A_framework_agreement_is_told_when_it_is_about_to_expire_and_when_it_is_nearly_used_up()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Alert Agreement PLC", Ct);
        var (fay, _, _) = await factory.UserWithAsync(admin, "alert.agreement.fay", company.Id, BuyingPermissions.AgreementsRead);
        using var fayClient = fay;
        var supplier = (await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = company.Id, name = "Agreement Supplier", type = "Supplier" }, Ct)).Content.ReadFromJsonAsync<PartyDto>(Ct))!;
        var cement = (await (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = company.Id, code = "CEM", name = "Cement", uom = "bag", isStockItem = true }, Ct)).Content.ReadFromJsonAsync<ItemDto>(Ct))!;
        var sand = (await (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = company.Id, code = "SND", name = "Sand", uom = "m3", isStockItem = true }, Ct)).Content.ReadFromJsonAsync<ItemDto>(Ct))!;

        // Valid all of 2026. Cement 100 bags at 500, sand 20 m3 at 300; the amount is capped at 100,000.
        var agreement = (await (await admin.PostAsJsonAsync(U("/api/buying/agreements"), new
        {
            companyId = company.Id, supplierId = supplier.Id, agreementDate = "2026-01-01", validFrom = "2026-01-01", validTo = "2026-12-31", amountLimit = 100_000m,
            lines = new object[] { new { itemId = cement.Id, rate = 500m, plannedQty = 100m }, new { itemId = sand.Id, rate = 300m, plannedQty = 20m } },
        }, Ct)).Content.ReadFromJsonAsync<BlanketAgreementDto>(Ct))!;
        await admin.PostAsync(U($"/api/buying/agreements/{agreement.Id}/submit"), null, Ct);

        // Expiring: on 2026-12-01 it has 30 days left. Within 29 days it is not yet; within 30 it is. A draft would not count (this one is submitted).
        var expiring = await CreateRuleAsync(admin, company.Id, "buying.agreements-expiring", users: ["alert.agreement.fay"], threshold: 29);
        Assert.Equal((0, 0, 0), Counts(await RunAsync(admin, expiring.Id, "2026-12-01")));
        await admin.PutAsJsonAsync(U($"/api/core/alerts/rules/{expiring.Id}"), new { name = "Ending", threshold = 30, userNames = new[] { "alert.agreement.fay" }, roleNames = Array.Empty<string>() }, Ct);
        Assert.Equal((1, 1, 0), Counts(await RunAsync(admin, expiring.Id, "2026-12-01")));
        var ending = Assert.Single(await InboxAsync(fayClient));
        Assert.Equal(($"{agreement.Number}: Agreement Supplier, ends 2026-12-31 (30 days left)", "BlanketAgreement", agreement.Id), (ending.Title, ending.LinkKind, ending.LinkId));
        Assert.Equal((0, 0, 1), Counts(await RunAsync(admin, expiring.Id, "2027-01-02"))); // over: no longer "about to expire", so it clears

        // Used up: an order of 92 bags is 46,000 of 100,000 (46%) and 92% of the cement; sand is untouched (0%).
        var order = (await (await admin.PostAsJsonAsync(U("/api/buying/purchase-orders"), new
        {
            companyId = company.Id, supplierId = supplier.Id, orderDate = "2026-11-10", agreementId = agreement.Id, lines = new[] { new { itemId = cement.Id, qty = 92m, rate = 500m } },
        }, Ct)).Content.ReadFromJsonAsync<PurchaseOrderDto>(Ct))!;
        (await admin.PostAsync(U($"/api/buying/purchase-orders/{order.Id}/submit"), null, Ct)).EnsureSuccessStatusCode();

        var used = await CreateRuleAsync(admin, company.Id, "buying.agreements-nearly-exhausted", users: ["alert.agreement.fay"], threshold: 90);
        Assert.Equal((1, 1, 0), Counts(await RunAsync(admin, used.Id, "2026-11-15"))); // the cement only
        Assert.Contains("Cement, 92% of the planned quantity", (await InboxAsync(fayClient))[0].Title, StringComparison.Ordinal);
        await admin.PutAsJsonAsync(U($"/api/core/alerts/rules/{used.Id}"), new { name = "Used", threshold = 40, userNames = new[] { "alert.agreement.fay" }, roleNames = Array.Empty<string>() }, Ct);
        Assert.Equal((2, 1, 0), Counts(await RunAsync(admin, used.Id, "2026-11-15"))); // now the amount (46%) as well; the cement is not repeated
        Assert.Contains("46% of the amount", (await InboxAsync(fayClient))[0].Title, StringComparison.Ordinal);

        // Cancelling the order gives it all back: both clear.
        (await admin.PostAsync(U($"/api/buying/purchase-orders/{order.Id}/cancel"), null, Ct)).EnsureSuccessStatusCode();
        Assert.Equal((0, 0, 2), Counts(await RunAsync(admin, used.Id, "2026-11-15")));
    }

    [Fact]
    public async Task A_loan_is_told_when_only_a_few_instalments_are_left_and_not_while_paused()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Alert Loan PLC", Ct);
        var (gil, _, _) = await factory.UserWithAsync(admin, "alert.loan.gil", company.Id, PayrollPermissions.LoansRead);
        using var gilClient = gil;
        var employee = (await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = company.Id, firstName = "Almaz", fatherName = "Tesfaye", gender = "Female", dateOfJoining = "2025-01-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct))!;

        async Task<EmployeeLoanDto> Loan(string kind, decimal? principal, decimal instalment)
        {
            var created = await admin.PostAsJsonAsync(U("/api/payroll/loans"), new
            {
                companyId = company.Id, employeeId = employee.Id, kind, component = kind == "Loan" ? "Salary advance" : "Idir", abbreviation = kind == "Loan" ? null : "IDIR",
                principal, instalment, startMonth = "2026-09-01",
            }, Ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var draft = (await created.Content.ReadFromJsonAsync<EmployeeLoanDto>(Ct))!;
            return (await (await admin.PostAsync(U($"/api/payroll/loans/{draft.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<EmployeeLoanDto>(Ct))!;
        }

        // 2,500 at 1,000 a month is 3 instalments (1,000, 1,000, 500). A standing deduction has no end to run down to.
        var loan = await Loan("Loan", 2_500m, 1_000m);
        await Loan("StandingDeduction", null, 50m);

        var rule = await CreateRuleAsync(admin, company.Id, "payroll.loans-nearly-repaid", users: ["alert.loan.gil"], threshold: 2);
        Assert.Equal((0, 0, 0), Counts(await RunAsync(admin, rule.Id, "2026-10-01"))); // 3 left: more than 2
        await admin.PutAsJsonAsync(U($"/api/core/alerts/rules/{rule.Id}"), new { name = "Loans", threshold = 3, userNames = new[] { "alert.loan.gil" }, roleNames = Array.Empty<string>() }, Ct);
        Assert.Equal((1, 1, 0), Counts(await RunAsync(admin, rule.Id, "2026-10-01")));
        var note = Assert.Single(await InboxAsync(gilClient));
        Assert.Equal(($"{loan.EmployeeName} ({loan.EmployeeNumber}): {loan.Number}, 3 instalments left", "EmployeeLoan", loan.Id), (note.Title, note.LinkKind, note.LinkId));

        // Paused, it is not about to end: it clears, and when it resumes it is told again.
        (await admin.PostAsync(U($"/api/payroll/loans/{loan.Id}/pause"), null, Ct)).EnsureSuccessStatusCode();
        Assert.Equal((0, 0, 1), Counts(await RunAsync(admin, rule.Id, "2026-10-01")));
        (await admin.PostAsync(U($"/api/payroll/loans/{loan.Id}/resume"), null, Ct)).EnsureSuccessStatusCode();
        Assert.Equal((1, 1, 0), Counts(await RunAsync(admin, rule.Id, "2026-10-01")));
        Assert.Equal(2, (await InboxAsync(gilClient)).Count);
    }
}
