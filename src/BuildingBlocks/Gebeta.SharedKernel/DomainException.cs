namespace Gebeta.SharedKernel;

/// <summary>
/// A business rule was broken. <see cref="Code"/> is stable and machine-readable
/// (for example <c>document.not_draft</c>); the API turns it into a Problem Details response.
/// </summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
