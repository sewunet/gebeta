using System.Text;
using Gebeta.Core.Domain.Attachments;

namespace Gebeta.Core.Tests;

public class AttachmentTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n");

    [Fact]
    public void A_file_is_checked_by_its_type_its_first_bytes_and_its_size_and_served_with_gebetas_content_type()
    {
        var ok = Attachment.Create(Guid.NewGuid(), Guid.NewGuid(), @"C:\scans\..\court order.PDF", Pdf);
        Assert.True(ok.IsSuccess);
        Assert.Equal(("court order.PDF", "application/pdf", Pdf.Length), (ok.Value.Attachment.FileName, ok.Value.Attachment.ContentType, ok.Value.Attachment.Size));
        Assert.Equal(64, ok.Value.Attachment.Sha256.Length);

        Assert.True(Attachment.Create(Guid.NewGuid(), Guid.NewGuid(), "setup.exe", Pdf).IsFailure);                          // not an allowed type
        Assert.True(Attachment.Create(Guid.NewGuid(), Guid.NewGuid(), "invoice.pdf", "MZ\u0090\0program"u8.ToArray()).IsFailure); // a program renamed to .pdf
        Assert.True(Attachment.Create(Guid.NewGuid(), Guid.NewGuid(), "notes.txt", [0x41, 0x00, 0x42]).IsFailure);           // binary posing as text
        Assert.True(Attachment.Create(Guid.NewGuid(), Guid.NewGuid(), "empty.pdf", []).IsFailure);
        Assert.True(Attachment.Create(Guid.NewGuid(), Guid.NewGuid(), "big.pdf", [.. Pdf, .. new byte[Attachment.MaxBytes]]).IsFailure);
        Assert.True(Attachment.Create(Guid.NewGuid(), Guid.NewGuid(), "photo.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2]).IsSuccess);
    }
}
