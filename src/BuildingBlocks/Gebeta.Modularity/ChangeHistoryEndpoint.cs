using Gebeta.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Gebeta.Modularity;

/// <summary>
/// <c>GET /api/history/{id}</c>: the change history of any audited record, from whichever installed module keeps it (each module keeps its own,
/// <see cref="IChangeHistory"/>). Needs <see cref="HistoryRead"/> and access to the record's company.
/// </summary>
public static class ChangeHistoryEndpoint
{
    /// <summary>Granted with core.* (Core Administrator) and to the roles that keep records (see Core's seed).</summary>
    public const string HistoryRead = "core.history.read";

    public static IEndpointRouteBuilder MapChangeHistory(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet("/api/history/{id:guid}", async (Guid id, IEnumerable<IChangeHistory> modules, ICompanyAccess access, CancellationToken ct) =>
            {
                var rows = new List<ChangeRecord>();
                foreach (var module in modules)
                {
                    rows.AddRange(await module.ForAsync(id, ct).ConfigureAwait(false));
                }

                foreach (var company in rows.Where(r => r.CompanyId is not null).Select(r => r.CompanyId!.Value).Distinct())
                {
                    if (!await access.CanAccessAsync(company, ct).ConfigureAwait(false))
                    {
                        return Results.NotFound();
                    }
                }

                return Results.Ok(rows.OrderByDescending(r => r.At).ThenBy(r => r.Field, StringComparer.Ordinal).ToList());
            })
            .WithTags("Platform").WithName("ChangeHistory").RequirePermission(HistoryRead);
        return endpoints;
    }
}
