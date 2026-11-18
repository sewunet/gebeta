using Gebeta.Application;
using Gebeta.Core.Application.Companies;
using Gebeta.Core.Contracts;
using Gebeta.Core.Application.Employees;
using Gebeta.Core.Application.Security;
using Gebeta.Core.Domain.Employees;
using Gebeta.Core.Application.Settings;
using Gebeta.Core.Domain.Settings;
using Gebeta.Messaging;
using Gebeta.Persistence;
using Gebeta.Core.Domain.Companies;
using Gebeta.Core.Domain.Security;
using Gebeta.Core.Infrastructure.Persistence;
using Gebeta.Core.Presentation;
using Gebeta.Modularity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Core.Infrastructure;

/// <summary>
/// Core (Platform): the only required module. This class is the module's composition root:
/// it wires Domain, Application, Infrastructure and Presentation together.
/// </summary>
public sealed class CoreModule : IErpModule
{
    public const string Id = ModuleLoader.CoreModuleId;
    public const string ConnectionStringName = "Main";

    public ModuleInfo Info { get; } = new(Id, "Core", new Version(0, 2, 0));
    public IReadOnlyList<string> Requires { get; } = [];
    public IReadOnlyList<string> Enhances { get; } = [];

    public void Register(ModuleRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;
        var connectionString = context.Configuration.GetConnectionString(ConnectionStringName)
            ?? throw new ModuleLoadException(
                $"Connection string '{ConnectionStringName}' is missing (ConnectionStrings:{ConnectionStringName} or ConnectionStrings__{ConnectionStringName}).");

        services.AddSingleton(TimeProvider.System);

        services.AddDbContext<CoreDbContext>(options => ConfigureDbContext(options, connectionString));
        services.AddScoped<ICoreUnitOfWork>(sp => sp.GetRequiredService<CoreDbContext>());
        services.AddScoped<ICompanyRepository, CompanyRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();

        var security = new SecurityOptions();
        context.Configuration.GetSection(SecurityOptions.Section).Bind(security);
        services.AddSingleton(security);
        services.AddScoped<UserContext>();
        services.AddScoped<IPermissionChecker>(sp => sp.GetRequiredService<UserContext>());
        services.AddScoped<ICompanyAccess>(sp => sp.GetRequiredService<UserContext>());

        // Shared services every module uses: company facts, number patterns, gap-free numbering.
        services.AddScoped<INamingSeriesRepository, NamingSeriesRepository>();
        services.AddScoped<IHolidayRepository, HolidayRepository>();
        services.AddScoped<ICompanyDirectory, CompanyDirectory>();
        services.AddScoped<INamingSeriesSettings, NamingSeriesSettings>();
        services.AddScoped<DocumentNumbering>();
        services.AddScoped<ICoreNumbers, CoreNumbers>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<Gebeta.Core.Domain.Parties.IPartyRepository, PartyRepository>();
        services.AddScoped<Gebeta.Core.Domain.Items.IItemRepository, ItemRepository>();
        services.AddScoped<Gebeta.Core.Application.Items.ItemHandler>();
        services.AddScoped<IItemDirectory>(sp => sp.GetRequiredService<Gebeta.Core.Application.Items.ItemHandler>());
        services.AddScoped<ICommandHandler<Gebeta.Core.Application.Items.CreateItemCommand, ItemDto>>(sp => sp.GetRequiredService<Gebeta.Core.Application.Items.ItemHandler>());
        services.AddScoped<ICommandHandler<Gebeta.Core.Application.Items.UpdateItemCommand, ItemDto>>(sp => sp.GetRequiredService<Gebeta.Core.Application.Items.ItemHandler>());
        services.AddScoped<IQueryHandler<Gebeta.Core.Application.Items.ListItemsQuery, IReadOnlyList<ItemDto>>>(sp => sp.GetRequiredService<Gebeta.Core.Application.Items.ItemHandler>());
        services.AddScoped<IQueryHandler<Gebeta.Core.Application.Items.GetItemQuery, ItemDto>>(sp => sp.GetRequiredService<Gebeta.Core.Application.Items.ItemHandler>());
        services.AddScoped<Gebeta.Core.Application.Parties.PartyHandler>();
        services.AddScoped<IPartyDirectory>(sp => sp.GetRequiredService<Gebeta.Core.Application.Parties.PartyHandler>());
        services.AddScoped<ICommandHandler<Gebeta.Core.Application.Parties.CreatePartyCommand, PartyDto>>(sp => sp.GetRequiredService<Gebeta.Core.Application.Parties.PartyHandler>());
        services.AddScoped<IQueryHandler<Gebeta.Core.Application.Parties.ListPartiesQuery, IReadOnlyList<PartyDto>>>(sp => sp.GetRequiredService<Gebeta.Core.Application.Parties.PartyHandler>());
        services.AddScoped<IQueryHandler<Gebeta.Core.Application.Parties.GetPartyQuery, PartyDto>>(sp => sp.GetRequiredService<Gebeta.Core.Application.Parties.PartyHandler>());
        services.AddScoped<ICommandHandler<Gebeta.Core.Application.Parties.UpdatePartyCommand, PartyDto>>(sp => sp.GetRequiredService<Gebeta.Core.Application.Parties.PartyHandler>());
        services.AddScoped<EmployeeDirectory>();
        services.AddScoped<IEmployeeDirectory>(sp => sp.GetRequiredService<EmployeeDirectory>());
        services.AddScoped<IEmployeeList>(sp => sp.GetRequiredService<EmployeeDirectory>());
        services.AddScoped<EmployeeHandler>();
        services.AddScoped<ICommandHandler<CreateEmployeeCommand, EmployeeDto>>(sp => sp.GetRequiredService<EmployeeHandler>());
        services.AddScoped<IQueryHandler<ListEmployeesQuery, IReadOnlyList<EmployeeDto>>>(sp => sp.GetRequiredService<EmployeeHandler>());
        services.AddScoped<IQueryHandler<GetEmployeeQuery, EmployeeDto>>(sp => sp.GetRequiredService<EmployeeHandler>());
        services.AddScoped<ICommandHandler<UpdateEmployeeCommand, EmployeeDto>>(sp => sp.GetRequiredService<EmployeeHandler>());
        services.AddScoped<ICommandHandler<SetEmployeeLeavingCommand, EmployeeDto>>(sp => sp.GetRequiredService<EmployeeHandler>());
        services.AddScoped<IModuleMigrator, CoreMigrator>();
        services.AddOutboxDispatcher<CoreDbContext>(Id, CoreDbContext.SchemaName);
        services.AddHealthChecks().AddCheck<CoreDatabaseHealthCheck>("core-database");

        services.AddScoped<ICommandHandler<CreateCompanyCommand, CompanyDto>, CreateCompanyHandler>();
        services.AddScoped<IQueryHandler<ListCompaniesQuery, IReadOnlyList<CompanyDto>>, ListCompaniesHandler>();
        services.AddScoped<IQueryHandler<GetCompanyQuery, CompanyDto>, GetCompanyHandler>();
        services.AddScoped<ICommandHandler<UpdateCompanyCommand, CompanyDto>, UpdateCompanyHandler>();

        services.AddScoped<GetMeHandler>();
        services.AddScoped<IQueryHandler<GetMeQuery, MeDto>>(sp => sp.GetRequiredService<GetMeHandler>());
        services.AddScoped<ICommandHandler<UpdateMyPreferencesCommand, MeDto>, UpdateMyPreferencesHandler>();
        services.AddScoped<IQueryHandler<ListUsersQuery, IReadOnlyList<UserDto>>, ListUsersHandler>();
        services.AddScoped<UserRoleHandler>();
        services.AddScoped<ICommandHandler<AssignRoleCommand, UserDto>>(sp => sp.GetRequiredService<UserRoleHandler>());
        services.AddScoped<ICommandHandler<RemoveRoleCommand, UserDto>>(sp => sp.GetRequiredService<UserRoleHandler>());
        services.AddScoped<ICommandHandler<InviteUserCommand, UserDto>, InviteUserHandler>();
        services.AddScoped<ICommandHandler<SetUserActiveCommand, UserDto>>(sp => sp.GetRequiredService<UserRoleHandler>());
        services.AddScoped<CompanyAccessHandler>();
        services.AddScoped<ICommandHandler<GrantCompanyAccessCommand, UserDto>>(sp => sp.GetRequiredService<CompanyAccessHandler>());
        services.AddScoped<ICommandHandler<RevokeCompanyAccessCommand, UserDto>>(sp => sp.GetRequiredService<CompanyAccessHandler>());
        services.AddScoped<IQueryHandler<ListRolesQuery, IReadOnlyList<RoleDto>>, ListRolesHandler>();
        services.AddScoped<ICommandHandler<CreateRoleCommand, RoleDto>, CreateRoleHandler>();

        services.AddScoped<NamingSeriesHandler>();
        services.AddScoped<IQueryHandler<ListNamingSeriesQuery, IReadOnlyList<NamingSeriesDto>>>(sp => sp.GetRequiredService<NamingSeriesHandler>());
        services.AddScoped<ICommandHandler<SetNamingSeriesCommand, NamingSeriesDto>>(sp => sp.GetRequiredService<NamingSeriesHandler>());
        services.AddScoped<HolidayHandler>();
        services.AddScoped<IQueryHandler<ListHolidaysQuery, IReadOnlyList<HolidayDto>>>(sp => sp.GetRequiredService<HolidayHandler>());
        services.AddScoped<ICommandHandler<AddHolidayCommand, HolidayDto>>(sp => sp.GetRequiredService<HolidayHandler>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => CoreEndpoints.Map(endpoints);

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(CoreDbContext.MigrationsHistoryTable, CoreDbContext.SchemaName))
            .UseSnakeCaseNamingConvention();
}
