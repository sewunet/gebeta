using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Gebeta.Application;
using Gebeta.Core.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>Attachments (docs/requirements/sap-features.md #5): files on any record, within the companies the user may access.</summary>
public class AttachmentTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    private static MultipartFormDataContent Upload(Guid companyId, Guid entityId, string fileName, byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream"); // what the browser claims does not matter
        return new MultipartFormDataContent { { new StringContent(companyId.ToString()), "companyId" }, { new StringContent(entityId.ToString()), "entityId" }, { file, "file", fileName } };
    }

    [Fact]
    public async Task A_file_is_attached_listed_downloaded_shown_in_history_and_removed()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Attachment PLC", Ct);
        var other = await admin.NewCompanyAsync("Attachment Elsewhere PLC", Ct);
        var employee = (await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = company.Id, firstName = "Almaz", fatherName = "Test", gender = "Female", dateOfJoining = "2024-01-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct))!;
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n% court order\n");

        var added = await admin.PostAsync(U("/api/attachments"), Upload(company.Id, employee.Id, "court-order.pdf", pdf), Ct);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var file = (await added.Content.ReadFromJsonAsync<AttachmentDto>(Ct))!;
        Assert.Equal(("court-order.pdf", "application/pdf", (long)pdf.Length, "admin"), (file.FileName, file.ContentType, file.Size, file.UploadedBy));

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(U("/api/attachments"), Upload(company.Id, employee.Id, "run.exe", pdf), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsync(U("/api/attachments"), Upload(company.Id, employee.Id, "fake.pdf", "MZ program"u8.ToArray()), Ct)).StatusCode);

        var listed = (await admin.GetFromJsonAsync<List<AttachmentDto>>(U($"/api/attachments?companyId={company.Id}&entityId={employee.Id}"), Ct))!;
        Assert.Equal(file.Id, Assert.Single(listed).Id);

        var download = await admin.GetAsync(U($"/api/attachments/{file.Id}/content"), Ct);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType); // always a download, never shown in the page
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(pdf, await download.Content.ReadAsByteArrayAsync(Ct));

        // Adding the file shows in the employee's own history.
        var history = (await admin.GetFromJsonAsync<List<ChangeRecord>>(U($"/api/history/{employee.Id}"), Ct))!;
        Assert.Contains(history, h => h is { EntityType: "Attachment", Action: "Created" });

        // Another company's record, or a user without the company: nothing.
        Assert.Empty((await admin.GetFromJsonAsync<List<AttachmentDto>>(U($"/api/attachments?companyId={other.Id}&entityId={employee.Id}"), Ct))!);
        using var stranger = factory.ClientFor("attachment.stranger");
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync(U($"/api/attachments/{file.Id}/content"), Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync(U($"/api/attachments/{file.Id}"), Ct)).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<List<AttachmentDto>>(U($"/api/attachments?companyId={company.Id}&entityId={employee.Id}"), Ct))!);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(U($"/api/attachments/{file.Id}/content"), Ct)).StatusCode);
        Assert.Contains((await admin.GetFromJsonAsync<List<ChangeRecord>>(U($"/api/history/{employee.Id}"), Ct))!, h => h is { EntityType: "Attachment", Action: "Deleted" });
    }
}
