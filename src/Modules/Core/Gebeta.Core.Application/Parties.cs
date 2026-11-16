using Gebeta.Application;
using Gebeta.Core.Contracts;
using Gebeta.Core.Domain.Parties;
using Gebeta.SharedKernel;

namespace Gebeta.Core.Application.Parties;

public sealed record CreatePartyCommand(Guid CompanyId, string Name, PartyType Type, string? Tin, string? VatRegistrationNumber, string? Phone) : ICommand<PartyDto>;

public sealed record ListPartiesQuery(Guid CompanyId) : IQuery<IReadOnlyList<PartyDto>>;

public sealed record GetPartyQuery(Guid Id) : IQuery<PartyDto>;

public sealed record UpdatePartyCommand(Guid Id, string Name, PartyType Type, string? Tin, string? VatRegistrationNumber, string? Phone) : ICommand<PartyDto>;

public sealed class PartyHandler(IPartyRepository parties, ICompanyAccess access, Companies.ICoreUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<CreatePartyCommand, PartyDto>, IQueryHandler<ListPartiesQuery, IReadOnlyList<PartyDto>>, IPartyDirectory,
      IQueryHandler<GetPartyQuery, PartyDto>, ICommandHandler<UpdatePartyCommand, PartyDto>
{
    public async Task<Result<PartyDto>> Handle(GetPartyQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var party = await parties.GetAsync(query.Id, cancellationToken).ConfigureAwait(false);
        return party is null || !await access.CanAccessAsync(party.CompanyId, cancellationToken).ConfigureAwait(false)
            ? Error.NotFound("Party", query.Id)
            : ToPartyDto(party);
    }

    public async Task<Result<PartyDto>> Handle(UpdatePartyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var party = await parties.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (party is null || !await access.CanAccessAsync(party.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Party", command.Id);
        }

        var updated = party.Update(command.Name, command.Type, command.Tin, command.VatRegistrationNumber, command.Phone);
        if (updated.IsFailure)
        {
            return updated.Error!;
        }

        party.ModifiedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToPartyDto(party);
    }

    private static PartyDto ToPartyDto(Party p) => new(p.Id, p.CompanyId, p.Name, p.Type.ToString(), p.Tin?.Value, p.VatRegistrationNumber, p.Phone);

    public async Task<Result<PartyDto>> Handle(CreatePartyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        var created = Party.Create(command.CompanyId, command.Name, command.Type, command.Tin, command.VatRegistrationNumber, command.Phone);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        created.Value.CreatedAt = clock.GetUtcNow();
        parties.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(created.Value);
    }

    public async Task<Result<IReadOnlyList<PartyDto>>> Handle(ListPartiesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        return Result.Success<IReadOnlyList<PartyDto>>((await parties.ListAsync(query.CompanyId, cancellationToken).ConfigureAwait(false)).Select(ToDto).ToList());
    }

    public async Task<PartyInfo?> GetAsync(Guid partyId, CancellationToken cancellationToken)
    {
        var p = await parties.GetAsync(partyId, cancellationToken).ConfigureAwait(false);
        return p is null ? null : new PartyInfo(p.Id, p.CompanyId, p.Name, p.Type.ToString(), p.Tin?.Value, p.IsVatRegistered);
    }

    private static PartyDto ToDto(Party p) => new(p.Id, p.CompanyId, p.Name, p.Type.ToString(), p.Tin?.Value, p.VatRegistrationNumber, p.Phone);
}
