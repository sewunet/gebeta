using System.Collections.Concurrent;
using Gebeta.Application;

namespace Gebeta.Api.Tests;

/// <summary>
/// An alert condition the tests steer: what it returns, and whether it fails, is set per company, so tests on different companies never meet.
/// It reads what <c>core.companies.read</c> shows, a permission the test roles can be given or denied.
/// </summary>
public sealed class ControllableAlertCondition : IAlertCondition
{
    public static ControllableAlertCondition Instance { get; } = new();

    private readonly ConcurrentDictionary<Guid, IReadOnlyList<string>> _items = new();
    private readonly ConcurrentDictionary<Guid, bool> _failing = new();

    public string Id => "test.controllable";
    public string Name => "Controllable condition";
    public string Description => "Returns what the test says.";
    public string RequiredPermission => "core.companies.read";
    public AlertThreshold? Threshold => null;

    /// <summary>Makes exactly these keys match for the company; each is an item titled "Item {key}".</summary>
    public void Match(Guid companyId, params string[] keys) => _items[companyId] = keys;

    public void Fail(Guid companyId, bool failing) => _failing[companyId] = failing;

    public Task<IReadOnlyList<AlertItem>> EvaluateAsync(AlertContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_failing.TryGetValue(context.CompanyId, out var failing) && failing)
        {
            throw new InvalidOperationException("The condition is broken on purpose.");
        }

        var keys = _items.TryGetValue(context.CompanyId, out var found) ? found : [];
        return Task.FromResult<IReadOnlyList<AlertItem>>(keys.Select(k => new AlertItem(k, $"Item {k}")).ToList());
    }
}
