using Gebeta.SharedKernel;
using Gebeta.SharedKernel.Ethiopia;

namespace Gebeta.Core.Domain.Parties;

public enum PartyType
{
    Customer,
    Supplier,
    Both,
}

/// <summary>
/// A customer or supplier in the shared directory (docs/plugin-architecture.md). Tax invoices to and from
/// Ethiopian businesses need the TIN; VAT applies only when the party (or we) are VAT-registered.
/// </summary>
public sealed class Party : Entity
{
    private Party()
    {
        Name = string.Empty;
    }

    public Guid CompanyId { get; private set; }
    public string Name { get; private set; }
    public PartyType Type { get; private set; }
    public TaxpayerIdentificationNumber? Tin { get; private set; }
    public string? VatRegistrationNumber { get; private set; }
    public string? Phone { get; private set; }

    public bool IsVatRegistered => VatRegistrationNumber is not null;

    internal void CopyFrom(Party other) =>
        (Name, Type, Tin, VatRegistrationNumber, Phone) = (other.Name, other.Type, other.Tin, other.VatRegistrationNumber, other.Phone);

    public static Result<Party> Create(Guid companyId, string name, PartyType type, string? tin, string? vatRegistrationNumber, string? phone)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 140)
        {
            return Error.Validation("A name is required (at most 140 characters).");
        }

        TaxpayerIdentificationNumber? parsed = null;
        if (!string.IsNullOrWhiteSpace(tin))
        {
            var result = TaxpayerIdentificationNumber.Create(tin);
            if (result.IsFailure)
            {
                return result.Error!;
            }

            parsed = result.Value;
        }

        return new Party
        {
            CompanyId = companyId, Name = name.Trim(), Type = type, Tin = parsed,
            VatRegistrationNumber = string.IsNullOrWhiteSpace(vatRegistrationNumber) ? null : vatRegistrationNumber.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
        };
    }
}

public static class PartyEditing
{
    /// <summary>Changes a party's details; invoices keep the name and TIN they were issued with.</summary>
    public static Result Update(this Party party, string name, PartyType type, string? tin, string? vatRegistrationNumber, string? phone)
    {
        ArgumentNullException.ThrowIfNull(party);
        var check = Party.Create(party.CompanyId, name, type, tin, vatRegistrationNumber, phone);
        if (check.IsFailure)
        {
            return Result.Failure(check.Error!);
        }

        party.CopyFrom(check.Value);
        return Result.Success();
    }
}

public interface IPartyRepository
{
    Task<Party?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Party>> ListAsync(Guid companyId, CancellationToken cancellationToken);
    void Add(Party party);
}
