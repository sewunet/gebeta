namespace Gebeta.SharedKernel;

/// <summary>
/// Marks an entity whose changes are kept as history: who changed which field, when, from what to what (docs/requirements/sap-features.md #4).
/// Every document is audited (<see cref="DocumentBase"/>); master records opt in. Large list columns are recorded as "changed", not by value.
/// </summary>
public interface IAudited
{
    /// <summary>
    /// The record whose history shows this change; normally the entity itself (null). A part kept elsewhere names its owner, so an employee's
    /// history also shows changes to their payroll profile and salary.
    /// </summary>
    Guid? HistoryOwnerId => null;
}
