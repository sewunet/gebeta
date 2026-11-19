namespace Gebeta.Application;

/// <summary>The person making the current request, as the identity provider (Keycloak) describes them.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The <c>sub</c> claim: stable id of the user in Keycloak.</summary>
    string? ExternalId { get; }

    string? Username { get; }
    string? Email { get; }
    string? FullName { get; }
}

/// <summary>
/// Answers "may the current user do this?" for a permission such as <c>payroll.salary-slips.submit</c>.
/// Provided by the Core module; every module's endpoints use it through <c>RequirePermission</c>.
/// </summary>
public interface IPermissionChecker
{
    Task<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken);
}

/// <summary>
/// Which companies the current user may work in. Every module filters its records with it, so a
/// clerk of one sugar factory never sees another factory's documents. Provided by the Core module.
/// </summary>
public interface ICompanyAccess
{
    /// <summary>The accessible company ids, or null when the user may access every company.</summary>
    Task<IReadOnlySet<Guid>?> GetAccessibleCompanyIdsAsync(CancellationToken cancellationToken);

    Task<bool> CanAccessAsync(Guid companyId, CancellationToken cancellationToken);
}

/// <summary>
/// The employee record of the signed-in user, for self-service (my payslips, my leave). Provided by Core; null when the user is not
/// linked to an employee. Self-service never takes an employee id from the request: it always asks this.
/// </summary>
public interface ICurrentEmployee
{
    Task<Guid?> EmployeeIdAsync(CancellationToken cancellationToken);
}

/// <summary>One recorded change of an audited record. Action: Created, Changed or Deleted; Field and values only for Changed.</summary>
public sealed record ChangeRecord(Guid EntityId, string EntityType, Guid? CompanyId, DateTimeOffset At, string By, string Action, string? Field, string? OldValue,
    string? NewValue);

/// <summary>The change history a module keeps of its own records (each module's schema has its own); the platform endpoint asks them all.</summary>
public interface IChangeHistory
{
    Task<IReadOnlyList<ChangeRecord>> ForAsync(Guid entityId, CancellationToken cancellationToken);
}


/// <summary>A document related to the one being viewed. Id is null for a reference kept only as a number until a module recognises it.</summary>
public sealed record RelatedDocument(string Type, Guid? Id, string? Number, DateOnly? Date, string? Status, decimal? Amount);

/// <summary>
/// The documents a module holds that relate to a given document (docs/requirements/sap-features.md #2): by the document's id, or by its number for
/// links kept as a reference (an order number on an invoice or a goods receipt). The platform endpoint asks every installed module.
/// </summary>
public interface IRelatedDocuments
{
    Task<IReadOnlyList<RelatedDocument>> ForAsync(Guid companyId, Guid id, string? number, CancellationToken cancellationToken);

    /// <summary>This module's document with that number, to turn a reference into a link; null when it is not one of this module's.</summary>
    Task<RelatedDocument?> FindAsync(Guid companyId, string number, CancellationToken cancellationToken);
}

/// <summary>The permissions that guard one record: reading it, and changing it (what adding or removing a file on it needs).</summary>
public sealed record RecordPermissions(string Read, string Write);

/// <summary>
/// Which permissions guard a record, answered by the module that owns it (docs/requirements/sap-features.md #5). Attachments live in Core and name a
/// record only by id and company; Core asks every installed module and follows the answer, so a file is as private as its record. A record no
/// installed module claims is refused (deny by default).
/// </summary>
public interface IRecordPermissions
{
    /// <summary>The permissions of this module's record with that id in that company; null when it is not one of this module's records.</summary>
    Task<RecordPermissions?> ForAsync(Guid companyId, Guid recordId, CancellationToken cancellationToken);
}
