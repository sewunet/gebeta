using Gebeta.Core.Domain.Attachments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gebeta.Core.Infrastructure.Persistence;

internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("attachment");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.HasIndex(a => new { a.CompanyId, a.EntityId });
        builder.Property(a => a.FileName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(a => a.CreatedBy).HasMaxLength(140);
        builder.Property(a => a.ModifiedBy).HasMaxLength(140);
        builder.Ignore(a => a.DomainEvents);
    }
}

/// <summary>The bytes, in their own table: lists of attachments never read them, and they go into the database backups with their records.</summary>
internal sealed class AttachmentContentConfiguration : IEntityTypeConfiguration<AttachmentContent>
{
    public void Configure(EntityTypeBuilder<AttachmentContent> builder)
    {
        builder.ToTable("attachment_content");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Data).IsRequired();
        builder.HasOne<Attachment>().WithOne().HasForeignKey<AttachmentContent>(c => c.Id).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AttachmentRepository(CoreDbContext db) : IAttachmentRepository
{
    public Task<Attachment?> GetAsync(Guid id, CancellationToken cancellationToken) => db.Set<Attachment>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Attachment>> ForEntityAsync(Guid companyId, Guid entityId, CancellationToken cancellationToken) =>
        await db.Set<Attachment>().AsNoTracking().Where(a => a.CompanyId == companyId && a.EntityId == entityId).OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<byte[]?> ContentAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Set<AttachmentContent>().AsNoTracking().Where(c => c.Id == id).Select(c => c.Data).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public void Add(Attachment attachment, AttachmentContent content)
    {
        db.Set<Attachment>().Add(attachment);
        db.Set<AttachmentContent>().Add(content);
    }

    public async Task RemoveAsync(Attachment attachment, CancellationToken cancellationToken)
    {
        // The content goes with it (cascade); removing it here too keeps the change tracker consistent.
        if (await db.Set<AttachmentContent>().FirstOrDefaultAsync(c => c.Id == attachment.Id, cancellationToken).ConfigureAwait(false) is { } content)
        {
            db.Set<AttachmentContent>().Remove(content);
        }

        db.Set<Attachment>().Remove(attachment);
    }
}
