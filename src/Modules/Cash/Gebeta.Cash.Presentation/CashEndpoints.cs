using Gebeta.Application;
using Gebeta.Cash.Application;
using Gebeta.Cash.Contracts;
using Gebeta.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Gebeta.Cash.Presentation;

public static class CashEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var cash = endpoints.MapGroup("/api/cash").WithTags("Cash Management");

        cash.MapPut("/companies/{companyId:guid}/settings", async (Guid companyId, SettingsBody b, ICommandHandler<UpdateCashSettingsCommand, CashSettingsDto> h, CancellationToken ct) =>
                (await h.Handle(new UpdateCashSettingsCommand(companyId, b.EnableAutoRequest, b.MinimumBalance, b.TargetBalance, b.DefaultCashAccountCode, b.DefaultSourceAccountCode), ct)
                    .ConfigureAwait(false)).ToHttp())
            .WithName("UpdateCashSettings").RequirePermission(CashPermissions.SettingsManage);

        cash.MapGet("/cashiers", async (Guid companyId, IQueryHandler<ListCashiersQuery, IReadOnlyList<CashierDto>> h, CancellationToken ct) =>
                (await h.Handle(new ListCashiersQuery(companyId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListCashiers").RequirePermission(CashPermissions.CashiersRead);

        cash.MapPost("/cashiers", async (CreateCashierCommand command, ICommandHandler<CreateCashierCommand, CashierDto> h, CancellationToken ct) =>
            {
                var result = await h.Handle(command, ct).ConfigureAwait(false);
                return result.IsSuccess ? Results.Created($"/api/cash/cashiers/{result.Value.Id}", result.Value) : result.ToHttp();
            })
            .WithName("CreateCashier").RequirePermission(CashPermissions.CashiersManage);

        cash.MapGet("/cashiers/{id:guid}/ledger", async (Guid id, IQueryHandler<CashierLedgerQuery, IReadOnlyList<CashLedgerEntryDto>> h, CancellationToken ct) =>
                (await h.Handle(new CashierLedgerQuery(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("CashierLedger").RequirePermission(CashPermissions.CashiersRead);

        cash.MapPost("/transactions", async (CashTransactionCommand command, ICommandHandler<CashTransactionCommand, CashMovementDto> h, CancellationToken ct) =>
                (await h.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("CashTransaction").RequirePermission(CashPermissions.TransactionsCreate);

        cash.MapPost("/transfers", async (CashTransferCommand command, ICommandHandler<CashTransferCommand, CashMovementDto> h, CancellationToken ct) =>
                (await h.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("CashTransfer").RequirePermission(CashPermissions.TransactionsCreate);

        cash.MapGet("/petty-cash-requests", async (Guid companyId, IQueryHandler<ListPettyCashQuery, IReadOnlyList<PettyCashRequestDto>> h, CancellationToken ct) =>
                (await h.Handle(new ListPettyCashQuery(companyId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListPettyCash").RequirePermission(CashPermissions.CashiersRead);

        cash.MapPost("/petty-cash-requests", async (RequestPettyCashCommand command, ICommandHandler<RequestPettyCashCommand, PettyCashRequestDto> h, CancellationToken ct) =>
                (await h.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("RequestPettyCash").RequirePermission(CashPermissions.PettyCashRequest);

        cash.MapPost("/petty-cash-requests/{id:guid}/approve", async (Guid id, ApproveBody b, ICommandHandler<ApprovePettyCashCommand, PettyCashRequestDto> h, CancellationToken ct) =>
                (await h.Handle(new ApprovePettyCashCommand(id, b.FundingFrom, b.SourceAccountCode, b.FundingCashierId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ApprovePettyCash").RequirePermission(CashPermissions.PettyCashApprove);

        cash.MapPost("/petty-cash-requests/{id:guid}/reject", async (Guid id, RejectBody b, ICommandHandler<RejectPettyCashCommand, PettyCashRequestDto> h, CancellationToken ct) =>
                (await h.Handle(new RejectPettyCashCommand(id, b.Reason), ct).ConfigureAwait(false)).ToHttp())
            .WithName("RejectPettyCash").RequirePermission(CashPermissions.PettyCashApprove);
    }
}

public sealed record SettingsBody(bool EnableAutoRequest, decimal MinimumBalance, decimal TargetBalance, string? DefaultCashAccountCode, string? DefaultSourceAccountCode);

public sealed record ApproveBody(Gebeta.Cash.Domain.Cashiers.FundingFrom FundingFrom, string? SourceAccountCode, Guid? FundingCashierId);

public sealed record RejectBody(string Reason);
