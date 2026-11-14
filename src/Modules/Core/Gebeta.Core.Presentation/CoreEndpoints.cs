using Gebeta.Application;
using Gebeta.Core.Application.Companies;
using Gebeta.Core.Contracts;
using Gebeta.Modularity;
using Gebeta.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Gebeta.Core.Presentation;

public static class CoreEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // The frontend uses this to decide which screens, menu items and slots to show.
        endpoints.MapGet("/api/modules", (ModuleRegistry registry) => TypedResults.Ok(ModulesResponse.From(registry)))
            .WithName("GetModules")
            .WithTags("Core");

        var companies = endpoints.MapGroup("/api/core/companies").WithTags("Core: Companies");

        companies.MapGet("/", async (IQueryHandler<ListCompaniesQuery, IReadOnlyList<CompanyDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new ListCompaniesQuery(), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListCompanies");

        companies.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetCompanyQuery, CompanyDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetCompanyQuery(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GetCompany");

        companies.MapPost("/", async (CreateCompanyCommand command, ICommandHandler<CreateCompanyCommand, CompanyDto> handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(command, ct).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Created($"/api/core/companies/{result.Value.Id}", result.Value)
                    : result.ToHttp();
            })
            .WithName("CreateCompany");
    }
}

/// <summary>Maps application results to HTTP: 200, or Problem Details with 400 / 404 / 409.</summary>
public static class ResultHttpExtensions
{
    public static IResult ToHttp<T>(this Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        var error = result.Error!;
        var status = error.Code switch
        {
            "not_found" => StatusCodes.Status404NotFound,
            "conflict" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return TypedResults.Problem(title: error.Message, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}

public sealed record ModulesResponse(IReadOnlyList<ModuleDto> Modules, IReadOnlyList<CapabilityBinding> Capabilities)
{
    public static ModulesResponse From(ModuleRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return new ModulesResponse(
            registry.Modules.Select(m => new ModuleDto(m.Info.Id, m.Info.Name, m.Info.Version.ToString(), m.Enhances)).ToList(),
            registry.Capabilities);
    }
}

public sealed record ModuleDto(string Id, string Name, string Version, IReadOnlyList<string> Enhances);
