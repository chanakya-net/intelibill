using System.Net;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0611Tests
{
    [Fact]
    public async Task AttachRequiresLiveBillingRecentMfaAndMutationProof()
    {
        await using var fixture = await StartAsync(database);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        using var billing = await SignInBillingAsync(fixture);
        var path = Path(purchase.Quote);
        var metadata = Metadata(Pdf());
        var command = Command(metadata, Digest(await PreviewAsync(billing, path, metadata)));
        await AssertRoleDeniedAsync(fixture, path, command);
        using var noProof = await UploadAsync(billing, path, command, proof: "");
        Assert.Equal(HttpStatusCode.Forbidden, noProof.StatusCode);
        using var wrongOrigin = await UploadAsync(billing, path, command, origin: "https://different.example.test");
        Assert.Equal(HttpStatusCode.Forbidden, wrongOrigin.StatusCode);

        fixture.Trial.Catalogue.Clock.Advance(TimeSpan.FromMinutes(6));
        using var stale = await UploadAsync(billing, path, command);
        await AssertProblemAsync(stale, HttpStatusCode.Forbidden, "ManualPayment.RecentMfaRequired");
        Assert.Equal(0L, await fixture.CountAsync("billing_document_versions"));
    }

    [Fact]
    public async Task AccountOrderMismatchAndUnknownOrdersAreRefused()
    {
        await using var fixture = await StartAsync(database);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        var other = await fixture.AcceptedQuoteAsync(1);
        using var billing = await SignInBillingAsync(fixture);
        var metadata = Metadata(Pdf());
        var path = Path(purchase.Quote);
        var preview = await PreviewAsync(billing, path, metadata);
        foreach (var unknown in new[] { Guid.NewGuid(), other.Quote.OrderId })
        {
            var mismatched = Path(purchase.Quote with { OrderId = unknown });
            using var read = await billing.SendAsync(HttpMethod.Get, mismatched);
            await AssertProblemAsync(read, HttpStatusCode.NotFound, "BillingDocument.OrderNotFound");
            using var write = await UploadAsync(billing, mismatched, Command(metadata, Digest(preview)));
            await AssertProblemAsync(write, HttpStatusCode.NotFound, "BillingDocument.OrderNotFound");
        }
        var wrongAccount = Path(purchase.Quote with { AccountId = other.Account });
        using var previewWrongAccount = await P0212Fixture.PostAsync(billing, wrongAccount + "/preview", metadata);
        await AssertProblemAsync(previewWrongAccount, HttpStatusCode.NotFound, "BillingDocument.OrderNotFound");
        Assert.Equal(0L, await fixture.CountAsync("billing_document_versions"));
    }

    private static async Task AssertRoleDeniedAsync(P0302Fixture fixture, string path, object command)
    {
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        using var ownerWrite = await UploadAsync(owner, path, command);
        await AssertProblemAsync(ownerWrite, HttpStatusCode.Forbidden, "ManualPayment.AccessDenied");
        const string email = "documents.support@example.test";
        var seed = await EnrollAsync(fixture, owner, email, "support");
        using var support = await fixture.Trial.Catalogue.SignInOperatorAsync(seed, email);
        using var supportRead = await support.SendAsync(HttpMethod.Get, path);
        await AssertProblemAsync(supportRead, HttpStatusCode.Forbidden, "ManualPayment.AccessDenied");
        using var supportUpload = await UploadAsync(support, path, command);
        await AssertProblemAsync(supportUpload, HttpStatusCode.Forbidden, "ManualPayment.AccessDenied");
    }
}
