namespace Gebeta.Core.Contracts;

/// <summary>A configured number pattern, with an example of the next-but-unused number format.</summary>
public sealed record NamingSeriesDto(Guid Id, Guid CompanyId, string DocumentType, string Pattern, string Example);

/// <summary>A public holiday. Kind: Fixed, Movable (Orthodox Easter), Announced (entered by hand) or Company.</summary>
public sealed record HolidayDto(DateOnly Date, string EthiopianDate, string Name, string? AmharicName, string Kind, Guid? CompanyId);
