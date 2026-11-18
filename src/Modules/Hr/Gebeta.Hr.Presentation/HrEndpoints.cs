using Gebeta.Application;
using Gebeta.Hr.Application;
using Gebeta.Hr.Contracts;
using Gebeta.Hr.Domain.Leave;
using Gebeta.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Gebeta.Hr.Presentation;

public static class HrEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var hr = endpoints.MapGroup("/api/hr").WithTags("HR");

        hr.MapGet("/leave-applications", async (Guid companyId, DateOnly from, DateOnly to, IQueryHandler<ListLeaveQuery, IReadOnlyList<LeaveApplicationDto>> h, CancellationToken ct) =>
                (await h.Handle(new ListLeaveQuery(companyId, from, to), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListLeave").RequirePermission(HrPermissions.LeaveRead);

        hr.MapPost("/leave-applications", async (ApplyLeaveCommand command, ICommandHandler<ApplyLeaveCommand, LeaveApplicationDto> h, CancellationToken ct) =>
            {
                var result = await h.Handle(command, ct).ConfigureAwait(false);
                return result.IsSuccess ? Results.Created($"/api/hr/leave-applications/{result.Value.Id}", result.Value) : result.ToHttp();
            })
            .WithName("ApplyLeave").RequirePermission(HrPermissions.LeaveApply);

        hr.MapPost("/leave-applications/{id:guid}/approve", async (Guid id, ICommandHandler<ApproveLeaveCommand, LeaveApplicationDto> h, CancellationToken ct) =>
                (await h.Handle(new ApproveLeaveCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ApproveLeave").RequirePermission(HrPermissions.LeaveApprove);

        hr.MapPost("/leave-applications/{id:guid}/cancel", async (Guid id, ICommandHandler<CancelLeaveCommand, LeaveApplicationDto> h, CancellationToken ct) =>
                (await h.Handle(new CancelLeaveCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("CancelLeave").RequirePermission(HrPermissions.LeaveApprove);

        hr.MapGet("/employees/{employeeId:guid}/leave-balance", async (Guid employeeId, IQueryHandler<LeaveBalanceQuery, IReadOnlyList<LeaveBalanceDto>> h, CancellationToken ct) =>
                (await h.Handle(new LeaveBalanceQuery(employeeId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("LeaveBalance").RequirePermission(HrPermissions.LeaveRead);

        // Self-service: always the signed-in employee's own leave.
        hr.MapGet("/my/leave", async (IQueryHandler<MyLeaveQuery, MyLeaveDto> h, CancellationToken ct) =>
                (await h.Handle(new MyLeaveQuery(), ct).ConfigureAwait(false)).ToHttp())
            .WithName("MyLeave").RequirePermission(HrPermissions.SelfRead);

        hr.MapPost("/my/leave-applications", async (ApplyMyLeaveCommand command, ICommandHandler<ApplyMyLeaveCommand, LeaveApplicationDto> h, CancellationToken ct) =>
            {
                var result = await h.Handle(command, ct).ConfigureAwait(false);
                return result.IsSuccess ? Results.Created($"/api/hr/leave-applications/{result.Value.Id}", result.Value) : result.ToHttp();
            })
            .WithName("ApplyMyLeave").RequirePermission(HrPermissions.SelfApply);

        hr.MapPost("/leave-allocations", async (AllocateAnnualLeaveCommand command, ICommandHandler<AllocateAnnualLeaveCommand, AllocationResultDto> h, CancellationToken ct) =>
                (await h.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("AllocateAnnualLeave").RequirePermission(HrPermissions.LeaveAllocate);

        hr.MapGet("/attendance", async (Guid companyId, DateOnly from, DateOnly to, IQueryHandler<ListAttendanceQuery, IReadOnlyList<AttendanceDto>> h, CancellationToken ct) =>
                (await h.Handle(new ListAttendanceQuery(companyId, from, to), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListAttendance").RequirePermission(HrPermissions.AttendanceRead);

        hr.MapPut("/attendance", async (MarkAttendanceCommand command, ICommandHandler<MarkAttendanceCommand, AttendanceDto> h, CancellationToken ct) =>
                (await h.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("MarkAttendance").RequirePermission(HrPermissions.AttendanceManage);

        hr.MapGet("/leave-rules", () => TypedResults.Ok(EthiopianLeaveRules.Rules.Values.Select(r => new
            {
                Type = r.Type.ToString(), r.IsPaid, Counting = r.Counting.ToString(), r.MaximumDays, r.UsesBalance, r.LegalBasis,
            })))
            .WithName("LeaveRules").RequirePermission(HrPermissions.LeaveRead);
    }
}
