using Gebeta.Application;
using Gebeta.Messaging;
using Gebeta.Modularity;
using Gebeta.Payroll.Application;
using Gebeta.Payroll.Contracts;
using Gebeta.Payroll.Domain.BackPay;
using Gebeta.Payroll.Domain.Salary;
using Gebeta.Payroll.Infrastructure.Persistence;
using Gebeta.Payroll.Presentation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Payroll.Infrastructure;

/// <summary>
/// Payroll (docs/modules/payroll.md). Runs on Core alone; HR adds payment days from attendance,
/// Accounting posts the payroll journal, Cash Management pays cash salaries.
/// </summary>
public sealed class PayrollModule : IErpModule
{
    public const string Id = "payroll";

    public ModuleInfo Info { get; } = new(Id, "Payroll", new Version(0, 2, 0));
    public IReadOnlyList<string> Requires { get; } = [ModuleLoader.CoreModuleId];
    public IReadOnlyList<string> Enhances { get; } = ["hr", "accounting", "cash"];

    public void Register(ModuleRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;
        var connectionString = context.Configuration.GetConnectionString("Main")
            ?? throw new ModuleLoadException("Connection string 'Main' is missing.");

        services.AddDbContext<PayrollDbContext>(options => PayrollDbContext.Configure(options, connectionString));
        services.AddScoped<IPayrollUnitOfWork>(sp => sp.GetRequiredService<PayrollDbContext>());
        services.AddScoped<IPayrollNumbers, PayrollNumbers>();
        services.AddScoped<ISalaryStructureRepository, SalaryStructureRepository>();
        services.AddScoped<ISalaryStructureAssignmentRepository, AssignmentRepository>();
        services.AddScoped<ISalarySlipRepository, SalarySlipRepository>();
        services.AddScoped<IBackPaymentRepository, BackPaymentRepository>();
        services.AddScoped<IAdditionalPayRepository, AdditionalPayRepository>();
        services.AddScoped<IPayrollRunRepository, PayrollRunRepository>();
        services.AddScoped<IPayrollProfileRepository, PayrollProfileRepository>();
        var payrollOptions = new PayrollOptions();
        context.Configuration.GetSection(PayrollOptions.Section).Bind(payrollOptions);
        services.AddSingleton(payrollOptions);
        services.AddScoped<IModuleMigrator, PayrollMigrator>();
        services.AddOutboxDispatcher<PayrollDbContext>(Id, PayrollDbContext.SchemaName);

        // Payment days: HR provides them from attendance when installed; otherwise joining/leaving dates.
        context.Capabilities.UseWithFallback<IPaymentDaysProvider, NoAttendanceData>();
        services.AddScoped<IPayrollPaymentDays, PayrollPaymentDays>();

        services.AddScoped<IQueryHandler<CalculateSalaryQuery, SalaryCalculationDto>, CalculateSalaryHandler>();
        services.AddScoped<IQueryHandler<ListTaxTablesQuery, IReadOnlyList<TaxTableDto>>, ListTaxTablesHandler>();

        services.AddScoped<SalaryStructureHandler>();
        services.AddScoped<ICommandHandler<CreateSalaryStructureCommand, SalaryStructureDto>>(sp => sp.GetRequiredService<SalaryStructureHandler>());
        services.AddScoped<IQueryHandler<ListSalaryStructuresQuery, IReadOnlyList<SalaryStructureDto>>>(sp => sp.GetRequiredService<SalaryStructureHandler>());

        services.AddScoped<AssignmentHandler>();
        services.AddScoped<ICommandHandler<AssignSalaryStructureCommand, SalaryStructureAssignmentDto>>(sp => sp.GetRequiredService<AssignmentHandler>());
        services.AddScoped<IQueryHandler<ListAssignmentsQuery, IReadOnlyList<SalaryStructureAssignmentDto>>>(sp => sp.GetRequiredService<AssignmentHandler>());

        services.AddScoped<SalarySlipHandler>();
        services.AddScoped<PayrollSelfServiceHandler>();
        services.AddScoped<IAdditionalSalaryRepository, AdditionalSalaryRepository>();
        services.AddScoped<AdditionalSalaryHandler>();
        services.AddScoped<ICommandHandler<CreateAdditionalSalaryCommand, AdditionalSalaryDto>>(sp => sp.GetRequiredService<AdditionalSalaryHandler>());
        services.AddScoped<ICommandHandler<SubmitAdditionalSalaryCommand, AdditionalSalaryDto>>(sp => sp.GetRequiredService<AdditionalSalaryHandler>());
        services.AddScoped<ICommandHandler<CancelAdditionalSalaryCommand, AdditionalSalaryDto>>(sp => sp.GetRequiredService<AdditionalSalaryHandler>());
        services.AddScoped<IQueryHandler<GetAdditionalSalaryQuery, AdditionalSalaryDto>>(sp => sp.GetRequiredService<AdditionalSalaryHandler>());
        services.AddScoped<IQueryHandler<ListAdditionalSalariesQuery, IReadOnlyList<AdditionalSalaryDto>>>(sp => sp.GetRequiredService<AdditionalSalaryHandler>());
        services.AddScoped<IQueryHandler<MySalarySlipsQuery, IReadOnlyList<SalarySlipDto>>>(sp => sp.GetRequiredService<PayrollSelfServiceHandler>());
        services.AddScoped<IQueryHandler<MySalarySlipQuery, SalarySlipDto>>(sp => sp.GetRequiredService<PayrollSelfServiceHandler>());
        services.AddScoped<ICommandHandler<CreateSalarySlipCommand, SalarySlipDto>>(sp => sp.GetRequiredService<SalarySlipHandler>());
        services.AddScoped<ICommandHandler<SubmitSalarySlipCommand, SalarySlipDto>>(sp => sp.GetRequiredService<SalarySlipHandler>());
        services.AddScoped<ICommandHandler<CancelSalarySlipCommand, SalarySlipDto>>(sp => sp.GetRequiredService<SalarySlipHandler>());
        services.AddScoped<ICommandHandler<RecordSalarySlipHistoryCommand, SalarySlipDto>, SlipHistoryHandler>();
        services.AddScoped<IQueryHandler<GetSalarySlipQuery, SalarySlipDto>>(sp => sp.GetRequiredService<SalarySlipHandler>());
        services.AddScoped<IQueryHandler<ListSalarySlipsQuery, IReadOnlyList<SalarySlipDto>>>(sp => sp.GetRequiredService<SalarySlipHandler>());

        services.AddScoped<BackPaymentHandler>();
        services.AddScoped<ICommandHandler<CreateBackPaymentCommand, SalaryBackPaymentDto>>(sp => sp.GetRequiredService<BackPaymentHandler>());
        services.AddScoped<ICommandHandler<SubmitBackPaymentCommand, SalaryBackPaymentDto>>(sp => sp.GetRequiredService<BackPaymentHandler>());
        services.AddScoped<ICommandHandler<CancelBackPaymentCommand, SalaryBackPaymentDto>>(sp => sp.GetRequiredService<BackPaymentHandler>());
        services.AddScoped<IQueryHandler<GetBackPaymentQuery, SalaryBackPaymentDto>>(sp => sp.GetRequiredService<BackPaymentHandler>());
        services.AddScoped<IQueryHandler<ListBackPaymentsQuery, IReadOnlyList<SalaryBackPaymentDto>>>(sp => sp.GetRequiredService<BackPaymentHandler>());

        services.AddScoped<ICommandHandler<UpdatePayrollProfileCommand, PayrollProfileDto>, PayrollProfileHandler>();
        services.AddScoped<PayrollRunHandler>();
        services.AddScoped<ICommandHandler<CreatePayrollRunCommand, PayrollRunDto>>(sp => sp.GetRequiredService<PayrollRunHandler>());
        services.AddScoped<ICommandHandler<GeneratePayrollRunCommand, PayrollRunDto>>(sp => sp.GetRequiredService<PayrollRunHandler>());
        services.AddScoped<ICommandHandler<SubmitPayrollRunCommand, PayrollRunDto>>(sp => sp.GetRequiredService<PayrollRunHandler>());
        services.AddScoped<IQueryHandler<GetPayrollRunQuery, PayrollRunDto>>(sp => sp.GetRequiredService<PayrollRunHandler>());
        services.AddScoped<PayrollReportsHandler>();
        services.AddScoped<IQueryHandler<IncomeTaxDeclarationQuery, IncomeTaxDeclarationDto>>(sp => sp.GetRequiredService<PayrollReportsHandler>());
        services.AddScoped<IQueryHandler<PensionScheduleQuery, PensionScheduleDto>>(sp => sp.GetRequiredService<PayrollReportsHandler>());
        services.AddScoped<IQueryHandler<BankTransferQuery, BankTransferDto>>(sp => sp.GetRequiredService<PayrollReportsHandler>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => PayrollEndpoints.Map(endpoints);
}
