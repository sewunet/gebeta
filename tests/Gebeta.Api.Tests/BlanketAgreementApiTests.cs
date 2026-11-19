using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Gebeta.Application;
using Gebeta.Buying.Contracts;
using Gebeta.Core.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>Framework agreements: purchase orders call off against them, the balance follows, and overdrawing is refused (docs/modules/buying.md).</summary>
public class BlanketAgreementApiTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    private sealed record Setup(Guid CompanyId, Guid SupplierId, Guid OtherSupplierId, Guid CementId, Guid SandId, Guid PaintId);

    private static async Task<Setup> SetupAsync(HttpClient admin, string name)
    {
        var company = await admin.NewCompanyAsync(name, Ct);
        var id = company.Id;
        var supplier = await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = id, name = "Addis Cement Factory", type = "Supplier" }, Ct)).Content.ReadFromJsonAsync<PartyDto>(Ct);
        var other = await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = id, name = "Another Supplier", type = "Supplier" }, Ct)).Content.ReadFromJsonAsync<PartyDto>(Ct);
        async Task<Guid> Item(string code, string uom) =>
            (await (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = id, code, name = code, uom, isStockItem = true }, Ct)).Content.ReadFromJsonAsync<ItemDto>(Ct))!.Id;
        return new Setup(id, supplier!.Id, other!.Id, await Item("CEM", "bag"), await Item("SAND", "m3"), await Item("PAINT", "l"));
    }

    private static async Task<BlanketAgreementDto> AgreementAsync(HttpClient admin, Setup s, object[] lines, decimal? amountLimit, string validFrom, string validTo, bool submit = true)
    {
        var created = await admin.PostAsJsonAsync(U("/api/buying/agreements"), new
        {
            companyId = s.CompanyId, supplierId = s.SupplierId, agreementDate = "2026-01-01", validFrom, validTo, amountLimit, lines,
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var agreement = (await created.Content.ReadFromJsonAsync<BlanketAgreementDto>(Ct))!;
        return submit ? (await (await admin.PostAsync(U($"/api/buying/agreements/{agreement.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<BlanketAgreementDto>(Ct))! : agreement;
    }

    private static Task<HttpResponseMessage> OrderAsync(HttpClient admin, Setup s, Guid agreementId, string date, Guid? supplierId, params (Guid Item, decimal Qty, decimal Rate)[] lines) =>
        admin.PostAsJsonAsync(U("/api/buying/purchase-orders"), new
        {
            companyId = s.CompanyId, supplierId = supplierId ?? s.SupplierId, orderDate = date, agreementId,
            lines = lines.Select(l => new { itemId = l.Item, qty = l.Qty, rate = l.Rate }).ToArray(),
        }, Ct);

    private static async Task<PurchaseOrderDto> SubmittedOrderAsync(HttpClient admin, Setup s, Guid agreementId, params (Guid Item, decimal Qty, decimal Rate)[] lines)
    {
        var created = await OrderAsync(admin, s, agreementId, "2026-11-10", null, lines);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var order = (await created.Content.ReadFromJsonAsync<PurchaseOrderDto>(Ct))!;
        var submitted = await admin.PostAsync(U($"/api/buying/purchase-orders/{order.Id}/submit"), null, Ct);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        return (await submitted.Content.ReadFromJsonAsync<PurchaseOrderDto>(Ct))!;
    }

    private static async Task<BlanketAgreementDto> ReloadAsync(HttpClient admin, Guid id) => (await admin.GetFromJsonAsync<BlanketAgreementDto>(U($"/api/buying/agreements/{id}"), Ct))!;

    private static async Task<string> ProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    [Fact]
    public async Task Orders_take_from_the_agreement_and_cancelling_one_gives_its_share_back()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var s = await SetupAsync(admin, "Framework Flow PLC");
        // Cement 100 bags at 500, sand 20 m3 at 300; no amount limit. Valid until 2099 so it stays active however long this test lives.
        var agreement = await AgreementAsync(admin, s,
            [new { itemId = s.CementId, rate = 500m, plannedQty = 100m }, new { itemId = s.SandId, rate = 300m, plannedQty = 20m }], null, "2026-01-01", "2099-12-31");
        Assert.StartsWith("BPA-", agreement.Number, StringComparison.Ordinal);
        Assert.Equal("Active", agreement.State);

        // Order A takes 60 bags: 60 x 500 = 30,000. Cement has 40 left; sand is untouched.
        var a = await SubmittedOrderAsync(admin, s, agreement.Id, (s.CementId, 60m, 500m));
        Assert.Equal(agreement.Number, a.AgreementNumber);
        var shown = await ReloadAsync(admin, agreement.Id);
        var cement = shown.Lines.Single(l => l.ItemId == s.CementId);
        Assert.Equal((60m, 30_000m, 40m), (cement.CalledQty, cement.CalledAmount, cement.RemainingQty));
        Assert.Equal(20m, shown.Lines.Single(l => l.ItemId == s.SandId).RemainingQty);
        Assert.Equal(30_000m, shown.CalledAmount);
        Assert.Null(shown.RemainingAmount); // no amount limit
        Assert.Equal(a.Id, Assert.Single(shown.CallOffs).OrderId);

        // 41 bags would overdraw by one: refused when written, naming the item and what is left.
        Assert.Contains("40", await ProblemAsync(await OrderAsync(admin, s, agreement.Id, "2026-11-10", null, (s.CementId, 41m, 500m))), StringComparison.Ordinal);

        // A draft takes nothing: two drafts of 25 both pass when written (40 are left), and neither changes the balance.
        var b = (await (await OrderAsync(admin, s, agreement.Id, "2026-11-10", null, (s.CementId, 25m, 500m))).Content.ReadFromJsonAsync<PurchaseOrderDto>(Ct))!;
        var c = (await (await OrderAsync(admin, s, agreement.Id, "2026-11-10", null, (s.CementId, 25m, 500m))).Content.ReadFromJsonAsync<PurchaseOrderDto>(Ct))!;
        Assert.Equal(40m, (await ReloadAsync(admin, agreement.Id)).Lines.Single(l => l.ItemId == s.CementId).RemainingQty);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(U($"/api/buying/purchase-orders/{b.Id}/submit"), null, Ct)).StatusCode); // 15 left
        // Submitting checks again: the second 25 no longer fits.
        Assert.Contains("15", await ProblemAsync(await admin.PostAsync(U($"/api/buying/purchase-orders/{c.Id}/submit"), null, Ct)), StringComparison.Ordinal);

        // The agreement cannot be cancelled while orders hold some of it.
        await ProblemAsync(await admin.PostAsync(U($"/api/buying/agreements/{agreement.Id}/cancel"), null, Ct));

        // Cancelling order A gives its 60 back: 100 - 25 (B) = 75 left, so C's 25 now fits.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(U($"/api/buying/purchase-orders/{a.Id}/cancel"), null, Ct)).StatusCode);
        var afterCancel = (await ReloadAsync(admin, agreement.Id)).Lines.Single(l => l.ItemId == s.CementId);
        Assert.Equal((25m, 75m), (afterCancel.CalledQty, afterCancel.RemainingQty));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(U($"/api/buying/purchase-orders/{c.Id}/submit"), null, Ct)).StatusCode);

        // Taking every item to its planned quantity exhausts the agreement: cement 50 + 50 = 100, sand 20.
        await SubmittedOrderAsync(admin, s, agreement.Id, (s.CementId, 50m, 500m), (s.SandId, 20m, 300m));
        var done = await ReloadAsync(admin, agreement.Id);
        Assert.Equal("Exhausted", done.State);
        Assert.Equal(56_000m, done.CalledAmount); // 100 x 500 + 20 x 300

        // Related documents: the order points at its agreement and the agreement at its orders.
        var related = await admin.GetFromJsonAsync<List<RelatedDocument>>(U($"/api/documents/{b.Id}/related?companyId={s.CompanyId}&number={Uri.EscapeDataString(b.Number!)}"), Ct);
        Assert.Contains(related!, d => d.Type == "BlanketAgreement" && d.Id == agreement.Id);
        var back = await admin.GetFromJsonAsync<List<RelatedDocument>>(U($"/api/documents/{agreement.Id}/related?companyId={s.CompanyId}"), Ct);
        Assert.Equal(3, back!.Count(d => d.Type == "PurchaseOrder")); // B, C and the last one; A is cancelled
    }

    [Fact]
    public async Task An_order_for_another_supplier_period_item_or_rate_is_refused()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var s = await SetupAsync(admin, "Framework Refusals PLC");
        var agreement = await AgreementAsync(admin, s, [new { itemId = s.CementId, rate = 500m, plannedQty = 100m }], null, "2026-01-01", "2026-12-31");

        await ProblemAsync(await OrderAsync(admin, s, agreement.Id, "2026-11-10", s.OtherSupplierId, (s.CementId, 1m, 500m))); // different supplier
        await ProblemAsync(await OrderAsync(admin, s, agreement.Id, "2025-12-31", null, (s.CementId, 1m, 500m))); // the day before it starts
        await ProblemAsync(await OrderAsync(admin, s, agreement.Id, "2027-01-01", null, (s.CementId, 1m, 500m))); // the day after it ends
        await ProblemAsync(await OrderAsync(admin, s, agreement.Id, "2026-11-10", null, (s.PaintId, 1m, 80m))); // not on the agreement
        await ProblemAsync(await OrderAsync(admin, s, agreement.Id, "2026-11-10", null, (s.CementId, 1m, 450m))); // not the agreed rate
        Assert.Equal(HttpStatusCode.OK, (await OrderAsync(admin, s, agreement.Id, "2026-12-31", null, (s.CementId, 1m, 500m))).StatusCode); // the last day is inside

        // An agreement that is still a draft, or has been cancelled, takes no orders.
        var draft = await AgreementAsync(admin, s, [new { itemId = s.CementId, rate = 500m, plannedQty = 10m }], null, "2026-01-01", "2026-12-31", submit: false);
        Assert.Equal("Draft", draft.State);
        await ProblemAsync(await OrderAsync(admin, s, draft.Id, "2026-11-10", null, (s.CementId, 1m, 500m)));
        var cancelled = await AgreementAsync(admin, s, [new { itemId = s.CementId, rate = 500m, plannedQty = 10m }], null, "2026-01-01", "2026-12-31");
        Assert.Equal("Cancelled", (await (await admin.PostAsync(U($"/api/buying/agreements/{cancelled.Id}/cancel"), null, Ct)).Content.ReadFromJsonAsync<BlanketAgreementDto>(Ct))!.State);
        await ProblemAsync(await OrderAsync(admin, s, cancelled.Id, "2026-11-10", null, (s.CementId, 1m, 500m)));

        // An agreement needs something to call off against, and a period that makes sense.
        await ProblemAsync(await admin.PostAsJsonAsync(U("/api/buying/agreements"), new
        {
            companyId = s.CompanyId, supplierId = s.SupplierId, agreementDate = "2026-01-01", validFrom = "2026-01-01", validTo = "2026-12-31",
            lines = new[] { new { itemId = s.CementId, rate = 500m } },
        }, Ct));
        await ProblemAsync(await admin.PostAsJsonAsync(U("/api/buying/agreements"), new
        {
            companyId = s.CompanyId, supplierId = s.SupplierId, agreementDate = "2026-01-01", validFrom = "2026-12-31", validTo = "2026-01-01", amountLimit = 1000m,
            lines = new[] { new { itemId = s.CementId, rate = 500m } },
        }, Ct));
    }

    [Fact]
    public async Task An_agreement_capped_by_amount_is_spent_across_its_items()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var s = await SetupAsync(admin, "Framework Amount PLC");
        // 10,000 for the whole agreement, no planned quantities.
        var agreement = await AgreementAsync(admin, s, [new { itemId = s.CementId, rate = 500m }, new { itemId = s.SandId, rate = 300m }], 10_000m, "2026-01-01", "2099-12-31");

        // 10 bags (5,000) + 10 m3 (3,000) = 8,000; 2,000 left.
        await SubmittedOrderAsync(admin, s, agreement.Id, (s.CementId, 10m, 500m), (s.SandId, 10m, 300m));
        var shown = await ReloadAsync(admin, agreement.Id);
        Assert.Equal((8_000m, 2_000m, "Active"), (shown.CalledAmount, shown.RemainingAmount ?? -1m, shown.State));
        Assert.All(shown.Lines, l => Assert.Null(l.RemainingQty));

        // 7 m3 is 2,100: over by 100. 4 bags is exactly 2,000: allowed, and spends it all.
        Assert.Contains("2000", (await ProblemAsync(await OrderAsync(admin, s, agreement.Id, "2026-11-10", null, (s.SandId, 7m, 300m)))).Replace(",", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        await SubmittedOrderAsync(admin, s, agreement.Id, (s.CementId, 4m, 500m));
        var spent = await ReloadAsync(admin, agreement.Id);
        Assert.Equal((10_000m, 0m, "Exhausted"), (spent.CalledAmount, spent.RemainingAmount ?? -1m, spent.State));
    }

    [Fact]
    public async Task Attachments_on_an_agreement_follow_the_agreements_permissions()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var s = await SetupAsync(admin, "Framework Files PLC");
        var agreement = await AgreementAsync(admin, s, [new { itemId = s.CementId, rate = 500m, plannedQty = 10m }], null, "2026-01-01", "2026-12-31");
        MultipartFormDataContent Upload()
        {
            var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\n% signed agreement\n"));
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            return new MultipartFormDataContent { { new StringContent(s.CompanyId.ToString()), "companyId" }, { new StringContent(agreement.Id.ToString()), "entityId" }, { file, "file", "agreement.pdf" } };
        }

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(U("/api/attachments"), Upload(), Ct)).StatusCode);
        async Task<HttpClient> UserWith(string username, params string[] permissions)
        {
            var client = factory.ClientFor(username);
            var me = (await client.GetFromJsonAsync<MeDto>(U("/api/me"), Ct))!;
            var role = (await (await admin.PostAsJsonAsync(U("/api/core/roles"), new { name = $"Role of {username}", permissions }, Ct)).Content.ReadFromJsonAsync<RoleDto>(Ct))!;
            (await admin.PutAsync(U($"/api/core/users/{me.Id}/roles/{role.Id}"), null, Ct)).EnsureSuccessStatusCode();
            (await admin.PutAsync(U($"/api/core/users/{me.Id}/companies/{s.CompanyId}"), null, Ct)).EnsureSuccessStatusCode();
            return client;
        }

        using var blind = await UserWith("agreement.blind", "core.attachments.read", "core.attachments.manage");
        Assert.Equal(HttpStatusCode.NotFound, (await blind.GetAsync(U($"/api/attachments?companyId={s.CompanyId}&entityId={agreement.Id}"), Ct)).StatusCode);
        using var reader = await UserWith("agreement.reader", "core.attachments.read", "core.attachments.manage", BuyingPermissions.AgreementsRead);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(U($"/api/attachments?companyId={s.CompanyId}&entityId={agreement.Id}"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.PostAsync(U("/api/attachments"), Upload(), Ct)).StatusCode); // adding needs agreements.create
    }
}
