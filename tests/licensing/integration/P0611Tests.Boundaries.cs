using System.Net;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0611Tests
{
    [Fact]
    public async Task ExactlyTenMiBAndTheNextIstDayAreAcceptedAtTheirBoundaries()
    {
        var clock = new P0201Clock { Now = Instant };
        await using var fixture = await P0302Fixture.StartAsync(database, clock, P0306Tests.Settings);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        using var billing = await SignInBillingAsync(fixture);
        var path = Path(purchase.Quote);
        var bytes = Pdf(MaxBytes);
        Assert.Equal(MaxBytes, bytes.Length);
        var metadata = Metadata(bytes);
        metadata["issuedOn"] = IssueDate.AddDays(1);
        using var early = await P0212Fixture.PostAsync(billing, path + "/preview", metadata);
        await AssertProblemAsync(early, HttpStatusCode.BadRequest, "BillingDocument.Invalid");
        clock.Now = Instant.AddSeconds(1);
        using var upload = await UploadAsync(billing, path, Command(metadata, Digest(await PreviewAsync(billing, path, metadata))), bytes);
        await ReadAsync(upload, HttpStatusCode.Created);
        Assert.Equal(bytes, await StoredBytesAsync(fixture));
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("missing")]
    [InlineData("reordered")]
    [InlineData("unknown-json-member")]
    [InlineData("duplicate-json-member")]
    [InlineData("wrong-xref")]
    [InlineData("non-multipart")]
    [InlineData("different-file-name")]
    public async Task MalformedMultipartJsonAndCrossReferenceAreRefused(string scenario)
    {
        await using var fixture = await StartAsync(database);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        using var billing = await SignInBillingAsync(fixture);
        var path = Path(purchase.Quote);
        using var form = new MultipartFormDataContent();
        var bytes = scenario == "wrong-xref" ? InvalidCrossReference() : Pdf();
        var command = Command(Metadata(bytes), new string('a', 64));
        if (scenario == "unknown-json-member") command["ignored"] = true;
        var json = System.Text.Json.JsonSerializer.Serialize(command);
        if (scenario == "duplicate-json-member") json = "{\"confirmed\":false," + json[1..];
        using var metadata = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new("application/pdf");
        if (scenario != "reordered") form.Add(metadata, "command");
        if (scenario != "missing") form.Add(file, "content", scenario == "different-file-name" ? "other.pdf" : "fixture-invoice.pdf");
        if (scenario == "extra") form.Add(new StringContent("extra"), "extra");
        if (scenario == "reordered") form.Add(metadata, "command");
        var content = scenario == "non-multipart" ? (HttpContent)metadata : form;
        using var upload = await billing.SendAsync(HttpMethod.Post, path, content, await P0206Fixture.ProofAsync(billing));
        var expected = scenario switch
        {
            "non-multipart" => (HttpStatusCode.UnsupportedMediaType, "BillingDocument.MediaTypeUnsupported"),
            "different-file-name" => (HttpStatusCode.BadRequest, "BillingDocument.ContentMismatch"),
            "wrong-xref" => (HttpStatusCode.BadRequest, "BillingDocument.ContentInvalid"),
            _ => (HttpStatusCode.BadRequest, "BillingDocument.Invalid"),
        };
        await AssertProblemAsync(upload, expected.Item1, expected.Item2);
        Assert.Equal(0L, await fixture.CountAsync("billing_document_versions"));
    }

    private static byte[] InvalidCrossReference()
    {
        var valid = System.Text.Encoding.ASCII.GetString(Pdf());
        return System.Text.Encoding.ASCII.GetBytes(valid[..valid.LastIndexOf("startxref", StringComparison.Ordinal)] + "startxref\n9\n%%EOF\n");
    }
}
