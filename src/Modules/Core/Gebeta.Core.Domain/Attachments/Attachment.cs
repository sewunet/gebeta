using System.Security.Cryptography;
using Gebeta.SharedKernel;

namespace Gebeta.Core.Domain.Attachments;

/// <summary>
/// A file attached to a record of any module (docs/requirements/sap-features.md #5): the supplier's invoice scan, the court order behind a standing
/// deduction, a contract. Kept by Core for every module; the record is named only by its id and company. Adding and removing show in the record's
/// history. The bytes are kept apart (<see cref="AttachmentContent"/>) so lists never load them.
/// </summary>
public sealed class Attachment : Entity, IAudited
{
    public const long MaxBytes = 10 * 1024 * 1024;

    /// <summary>What may be attached, by extension, with the content type it is served as. Anything else is refused.</summary>
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".csv"] = "text/csv",
        [".txt"] = "text/plain",
    };

    private Attachment()
    {
        FileName = string.Empty;
        ContentType = string.Empty;
        Sha256 = string.Empty;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>The record the file belongs to (an invoice, an employee, a loan...).</summary>
    public Guid EntityId { get; private set; }

    public string FileName { get; private set; }
    public string ContentType { get; private set; }
    public long Size { get; private set; }
    public string Sha256 { get; private set; }

    /// <summary>Adding or removing a file shows in the record's own history.</summary>
    Guid? IAudited.HistoryOwnerId => EntityId;

    /// <summary>
    /// Checks the file (allowed type by extension, the bytes look like that type, at most 10 MB) and returns the attachment and its content. The
    /// content type is Gebeta's own for the extension, never the browser's.
    /// </summary>
    public static Result<(Attachment Attachment, AttachmentContent Content)> Create(Guid companyId, Guid entityId, string fileName, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/')).Trim();
        if (name.Length is 0 or > 200 || name.Any(char.IsControl))
        {
            return Error.Validation("Give the file a name of at most 200 characters.");
        }

        if (!Types.TryGetValue(Path.GetExtension(name), out var contentType))
        {
            return Error.Validation("Attach a PDF, an image (PNG, JPG, WebP), a spreadsheet or document (XLSX, DOCX), or a CSV or text file.");
        }

        if (bytes.Length == 0 || bytes.Length > MaxBytes)
        {
            return Error.Validation($"A file is at most {MaxBytes / 1024 / 1024} MB, and not empty.");
        }

        if (!LooksLike(Path.GetExtension(name).ToLowerInvariant(), bytes))
        {
            return Error.Validation($"{name} is not really a {Path.GetExtension(name).TrimStart('.').ToUpperInvariant()} file.");
        }

        var attachment = new Attachment
        {
            CompanyId = companyId, EntityId = entityId, FileName = name, ContentType = contentType, Size = bytes.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
        };
        return (attachment, new AttachmentContent(attachment.Id, bytes));
    }

    /// <summary>The first bytes of each type, so a program renamed to .pdf is refused.</summary>
    private static bool LooksLike(string extension, byte[] b) => extension switch
    {
        ".pdf" => Starts(b, "%PDF-"u8),
        ".png" => Starts(b, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        ".jpg" or ".jpeg" => Starts(b, [0xFF, 0xD8, 0xFF]),
        ".webp" => b.Length > 12 && Starts(b, "RIFF"u8) && b.AsSpan(8, 4).SequenceEqual("WEBP"u8),
        ".xlsx" or ".docx" => Starts(b, [0x50, 0x4B, 0x03, 0x04]), // a zip, as Office files are
        _ => !b.AsSpan(0, Math.Min(b.Length, 8192)).Contains((byte)0), // text: no NUL bytes
    };

    private static bool Starts(byte[] bytes, ReadOnlySpan<byte> prefix) => bytes.AsSpan().StartsWith(prefix);
}

/// <summary>The bytes of an attachment, stored apart from its description.</summary>
public sealed class AttachmentContent(Guid id, byte[] data)
{
    public Guid Id { get; private set; } = id;
    public byte[] Data { get; private set; } = data;
}

public interface IAttachmentRepository
{
    Task<Attachment?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Attachment>> ForEntityAsync(Guid companyId, Guid entityId, CancellationToken cancellationToken);
    Task<byte[]?> ContentAsync(Guid id, CancellationToken cancellationToken);
    void Add(Attachment attachment, AttachmentContent content);
    Task RemoveAsync(Attachment attachment, CancellationToken cancellationToken);
}
