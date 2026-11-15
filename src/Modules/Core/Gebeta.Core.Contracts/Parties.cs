namespace Gebeta.Core.Contracts;

public sealed record PartyDto(Guid Id, Guid CompanyId, string Name, string Type, string? Tin, string? VatRegistrationNumber, string? Phone);
