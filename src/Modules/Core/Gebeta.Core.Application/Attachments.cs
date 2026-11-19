using Gebeta.Application;
using Gebeta.Core.Contracts;
using Gebeta.Core.Domain.Attachments;
using Gebeta.SharedKernel;

namespace Gebeta.Core.Application.Attachments;

public sealed record AddAttachmentCommand(Guid CompanyId, Guid EntityId, string FileName, byte[] Bytes) : ICommand<AttachmentDto>;

public sealed record RemoveAttachmentCommand(Guid Id) : ICommand<AttachmentDto>;

public sealed record ListAttachmentsQuery(Guid CompanyId, Guid EntityId) : IQuery<IReadOnlyList<AttachmentDto>>;

public sealed record AttachmentFile(string FileName, string ContentType, byte[] Bytes);

public sealed record GetAttachmentFileQuery(Guid Id) : IQuery<AttachmentFile>;

/// <summary>Files attached to records (docs/requirements/sap-features.md #5), within the companies the user may access.</summary>
public sealed class AttachmentHandler(IAttachmentRepository attachments, ICompanyAccess access, ICurrentUser currentUser, Companies.ICoreUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<AddAttachmentCommand, AttachmentDto>, ICommandHandler<RemoveAttachmentCommand, AttachmentDto>,
      IQueryHandler<ListAttachmentsQuery, IReadOnlyList<AttachmentDto>>, IQueryHandler<GetAttachmentFileQuery, AttachmentFile>
{
    public async Task<Result<AttachmentDto>> Handle(AddAttachmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        if (command.EntityId == Guid.Empty)
        {
            return Error.Validation("Say which record the file belongs to.");
        }

        var created = Attachment.Create(command.CompanyId, command.EntityId, command.FileName, command.Bytes);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var (attachment, content) = created.Value;
        attachment.CreatedAt = clock.GetUtcNow();
        attachment.CreatedBy = currentUser.Username;
        attachments.Add(attachment, content);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(attachment);
    }

    public async Task<Result<AttachmentDto>> Handle(RemoveAttachmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await AccessibleAsync(command.Id, cancellationToken).ConfigureAwait(false) is not { } attachment)
        {
            return Error.NotFound("Attachment", command.Id);
        }

        await attachments.RemoveAsync(attachment, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(attachment);
    }

    public async Task<Result<IReadOnlyList<AttachmentDto>>> Handle(ListAttachmentsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        return Result.Success<IReadOnlyList<AttachmentDto>>(
            (await attachments.ForEntityAsync(query.CompanyId, query.EntityId, cancellationToken).ConfigureAwait(false)).Select(ToDto).ToList());
    }

    public async Task<Result<AttachmentFile>> Handle(GetAttachmentFileQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await AccessibleAsync(query.Id, cancellationToken).ConfigureAwait(false) is not { } attachment
            || await attachments.ContentAsync(attachment.Id, cancellationToken).ConfigureAwait(false) is not { } bytes)
        {
            return Error.NotFound("Attachment", query.Id);
        }

        return new AttachmentFile(attachment.FileName, attachment.ContentType, bytes);
    }

    private async Task<Attachment?> AccessibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var attachment = await attachments.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return attachment is not null && await access.CanAccessAsync(attachment.CompanyId, cancellationToken).ConfigureAwait(false) ? attachment : null;
    }

    private static AttachmentDto ToDto(Attachment a) => new(a.Id, a.CompanyId, a.EntityId, a.FileName, a.ContentType, a.Size, a.CreatedAt, a.CreatedBy);
}
