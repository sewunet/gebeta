using Gebeta.Application;
using Gebeta.Hr.Application;
using Gebeta.Hr.Contracts;
using Gebeta.Hr.Infrastructure.Persistence;
using Gebeta.Hr.Presentation;
using Gebeta.Messaging;
using Gebeta.Modularity;
using Gebeta.Hr.Domain.Leave;
using Gebeta.Payroll.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Hr.Infrastructure;

/// <summary>
/// HR (docs/modules/hr.md): leave under Ethiopian labour law and attendance. Runs on Core alone;
/// with Payroll installed it provides payment days (unpaid leave, absences, sick leave beyond full pay).
/// </summary>
public sealed class HrModule : IErpModule
{
    public const string Id = "hr";

    public ModuleInfo Info { get; } = new(Id, "HR", new Version(0, 1, 0));
    public IReadOnlyList<string> Requires { get; } = [ModuleLoader.CoreModuleId];
    public IReadOnlyList<string> Enhances { get; } = ["payroll", "accounting", "cash"];

    public void Register(ModuleRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;
        var connectionString = context.Configuration.GetConnectionString("Main")
            ?? throw new ModuleLoadException("Connection string 'Main' is missing.");

        services.AddDbContext<HrDbContext>(options => HrDbContext.Configure(options, connectionString));
        services.AddScoped<IHrUnitOfWork>(sp => sp.GetRequiredService<HrDbContext>());
        services.AddScoped<IHrNumbers, HrNumbers>();
        services.AddScoped<ILeaveRepository, LeaveRepository>();
        services.AddScoped<IAttendanceRepository, AttendanceRepository>();
        services.AddScoped<IModuleMigrator, HrMigrator>();
        services.AddOutboxDispatcher<HrDbContext>(Id, HrDbContext.SchemaName);

        // Payment days for Payroll; only used when Payroll is installed.
        context.Capabilities.Provide<IPaymentDaysProvider, HrPaymentDays>();

        services.AddScoped<LeaveHandler>();
        services.AddScoped<ICommandHandler<ApplyLeaveCommand, LeaveApplicationDto>>(sp => sp.GetRequiredService<LeaveHandler>());
        services.AddScoped<ICommandHandler<ApproveLeaveCommand, LeaveApplicationDto>>(sp => sp.GetRequiredService<LeaveHandler>());
        services.AddScoped<ICommandHandler<CancelLeaveCommand, LeaveApplicationDto>>(sp => sp.GetRequiredService<LeaveHandler>());
        services.AddScoped<IQueryHandler<ListLeaveQuery, IReadOnlyList<LeaveApplicationDto>>>(sp => sp.GetRequiredService<LeaveHandler>());
        services.AddScoped<IQueryHandler<LeaveBalanceQuery, IReadOnlyList<LeaveBalanceDto>>>(sp => sp.GetRequiredService<LeaveHandler>());
        services.AddScoped<ICommandHandler<AllocateAnnualLeaveCommand, AllocationResultDto>>(sp => sp.GetRequiredService<LeaveHandler>());
        services.AddScoped<ICommandHandler<RecordOpeningLeaveCommand, LeaveBalanceDto>>(sp => sp.GetRequiredService<LeaveHandler>());
        services.AddScoped<AttendanceHandler>();
        services.AddScoped<ICommandHandler<MarkAttendanceCommand, AttendanceDto>>(sp => sp.GetRequiredService<AttendanceHandler>());
        services.AddScoped<IQueryHandler<ListAttendanceQuery, IReadOnlyList<AttendanceDto>>>(sp => sp.GetRequiredService<AttendanceHandler>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => HrEndpoints.Map(endpoints);
}
