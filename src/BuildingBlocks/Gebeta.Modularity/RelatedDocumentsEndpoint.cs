using Gebeta.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Gebeta.Modularity;

/// <summary>
/// <c>GET /api/documents/{id}/related?companyId&amp;number</c>: the documents before and after this one (order → receipt → invoice → payment →
/// journal; additional salary or loan → salary slip → journal), from every installed module (<see cref="IRelatedDocuments"/>). A related document
/// shows only to a user who may read that kind of document.
/// </summary>
public static class RelatedDocumentsEndpoint
{
    /// <summary>The permission that reads each kind of document; a kind not listed is not shown.</summary>
    private static readonly Dictionary<string, string> ReadPermission = new(StringComparer.Ordinal)
    {
        ["Invoice"] = "accounting.invoices.read",
        ["Payment"] = "accounting.invoices.read",
        ["JournalEntry"] = "accounting.journals.read",
        ["SalarySlip"] = "payroll.salary-slips.read",
        ["EmployeeLoan"] = "payroll.loans.read",
        ["AdditionalSalary"] = "payroll.additional-salary.read",
        ["BackPayment"] = "payroll.back-payments.read",
        ["PurchaseOrder"] = "buying.orders.read",
        ["SalesOrder"] = "selling.orders.read",
        ["StockEntry"] = "stock.entries.read",
        ["Order"] = "core.companies.read", // an order number no installed module knows
    };

    public static IEndpointRouteBuilder MapRelatedDocuments(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet("/api/documents/{id:guid}/related", async (Guid id, Guid companyId, string? number, IEnumerable<IRelatedDocuments> modules,
                ICompanyAccess access, IPermissionChecker permissions, CancellationToken ct) =>
            {
                if (!await access.CanAccessAsync(companyId, ct).ConfigureAwait(false))
                {
                    return Results.NotFound();
                }

                var providers = modules.ToList();
                var found = new List<RelatedDocument>();
                foreach (var module in providers)
                {
                    found.AddRange(await module.ForAsync(companyId, id, string.IsNullOrWhiteSpace(number) ? null : number.Trim(), ct).ConfigureAwait(false));
                }

                // References kept as a number (an order on an invoice) become links when a module knows the number.
                var resolved = new List<RelatedDocument>();
                foreach (var doc in found)
                {
                    var link = doc;
                    if (doc.Id is null && doc.Number is { } reference)
                    {
                        foreach (var module in providers)
                        {
                            if (await module.FindAsync(companyId, reference, ct).ConfigureAwait(false) is { } known)
                            {
                                link = known;
                                break;
                            }
                        }
                    }

                    resolved.Add(link);
                }

                var allowed = new Dictionary<string, bool>(StringComparer.Ordinal);
                var shown = new List<RelatedDocument>();
                foreach (var doc in resolved.Where(d => d.Id != id).DistinctBy(d => (d.Type, d.Id, d.Number)))
                {
                    if (!allowed.TryGetValue(doc.Type, out var may))
                    {
                        may = ReadPermission.TryGetValue(doc.Type, out var permission) && await permissions.HasPermissionAsync(permission, ct).ConfigureAwait(false);
                        allowed[doc.Type] = may;
                    }

                    if (may)
                    {
                        shown.Add(doc);
                    }
                }

                return Results.Ok(shown.OrderBy(d => d.Date ?? DateOnly.MaxValue).ThenBy(d => d.Number, StringComparer.Ordinal).ToList());
            })
            .WithTags("Platform").WithName("RelatedDocuments");
        return endpoints;
    }
}
