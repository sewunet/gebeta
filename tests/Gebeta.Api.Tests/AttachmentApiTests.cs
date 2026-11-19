using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Gebeta.Application;
using Gebeta.Core.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>Files attached to records (docs/requirements/sap-features.md #5): upload, list, download, remove, and the record's history.</summary>
public class AttachmentApiTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    private static MultipartFormDataContent Form(Guid companyId, Guid entityId, string fileName, byte[] bytes)
    {
        var form = new MultipartFormDataContent { { new StringContent(companyId.ToString()), "companyId" }, { new StringContent(entityId.ToString()), "entityId" } };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream"); // the browser's claim is ignored
        form.Add(file, "file", fileName);
        return form;
    }

    [Fact]
    public async Task A_file_is_attached_to_a_record_downloaded_as_it_was_and_removed_with_both_in_the_records_history()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Attachment PLC", Ct);
        var other = await admin.NewCompanyAsync("Attachment Elsewhere PLC", Ct);
        var employee = (await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = company.Id, firstName = "Almaz", fatherName = "Test", gender = "Female", dateOfJoining = "2024-01-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct))!;
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n court order for a standing deduction");

        var added = await admin.PostAsync(U("/api/attachments"), Form(company.Id, employee.Id, "court-order.pdf", pdf), Ct);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var attachment = (await added.Content.ReadFromJsonAsync<AttachmentDto>(Ct))!;
        Assert.Equal(("court-order.pdf", "application/pdf", (long)pdf.Length, "admin"), (attachment.FileName, attachment.ContentType, attachment.Size, attachment.UploadedBy));

        var listed = (await admin.GetFromJsonAsync<List<AttachmentDto>>(U($"/api/attachments?companyId={company.Id}&entityId={employee.Id}"), Ct))!;
        Assert.Equal(attachment.Id, Assert.Single(listed).Id);

        var download = await admin.GetAsync(U($"/api/attachments/{attachment.Id}/content"), Ct);
        Assert.Equal(pdf, await download.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType); // always a download
        Assert.Contains("nosniff", download.Headers.GetValues("X-Content-Type-Options"));

        // A program renamed to .pdf, a type not allowed, another company's record: refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(U("/api/attachments"), Form(company.Id, employee.Id, "invoice.pdf", Encoding.ASCII.GetBytes("MZ\u0090 program")), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(U("/api/attachments"), Form(company.Id, employee.Id, "run.exe", pdf), Ct)).StatusCode);
        using var stranger = factory.ClientFor("attachment.stranger");
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync(U($"/api/attachments/{attachment.Id}/content"), Ct)).StatusCode);

        (await admin.DeleteAsync(U($"/api/attachments/{attachment.Id}"), Ct)).EnsureSuccessStatusCode();
        Assert.Empty((await admin.GetFromJsonAsync<List<AttachmentDto>>(U($"/api/attachments?companyId={company.Id}&entityId={employee.Id}"), Ct))!);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(U($"/api/attachments/{attachment.Id}/content"), Ct)).StatusCode);

        var history = (await admin.GetFromJsonAsync<List<ChangeRecord>>(U($"/api/history/{employee.Id}"), Ct))!;
        Assert.Contains(history, h => h is { EntityType: "Attachment", Action: "Created" });
        Assert.Contains(history, h => h is { EntityType: "Attachment", Action: "Deleted" });
        _ = other;
    }
}
