using System.Net;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0611Tests
{
    [Fact]
    public async Task ConcurrentExactReplaysAndDistinctOperationsKeepOneInitialVersion()
    {
        await using var fixture = await StartAsync(database);
        var first = await fixture.AcceptedQuoteAsync(1);
        var second = await fixture.AcceptedQuoteAsync(1);
        using var billing = await SignInBillingAsync(fixture);
        await AssertConcurrentResultsAsync(billing, first.Quote, sameKey: true);
        await AssertConcurrentResultsAsync(billing, second.Quote, sameKey: false);
        Assert.Equal(2L, await fixture.CountAsync("billing_document_versions"));
        Assert.Equal(0L, await fixture.CountAsync("payment_confirmations"));
    }

    [Fact]
    public async Task RecoveryUsesTheOriginalOrderOwnerAndExactPayload()
    {
        await using var fixture = await StartAsync(database);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        using var billing = await SignInBillingAsync(fixture);
        var path = Path(purchase.Quote);
        var metadata = Metadata(Pdf());
        var preview = await PreviewAsync(billing, path, metadata);
        var stale = Command(metadata, new string('a', 64));
        using var rejected = await UploadAsync(billing, path, stale);
        await AssertProblemAsync(rejected, HttpStatusCode.Conflict, "BillingDocument.PreviewStale");

        var command = Command(metadata, Digest(preview));
        using var uploaded = await UploadAsync(billing, path, command);
        var receipt = await ReadAsync(uploaded, HttpStatusCode.Created);
        var originalOperation = $"{path}/operations/{command["operationKey"]}";
        using var recovered = await billing.SendAsync(HttpMethod.Get, originalOperation);
        Assert.Equal(receipt.GetRawText(), (await ReadAsync(recovered, HttpStatusCode.OK)).GetRawText());
        using var unknown = await billing.SendAsync(HttpMethod.Get, $"{path}/operations/{Guid.NewGuid()}");
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "BillingDocument.OperationNotFound");

        const string otherEmail = "other.finance@example.test";
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        var otherSeed = await EnrollAsync(fixture, owner, otherEmail, "billing");
        using var other = await fixture.Trial.Catalogue.SignInOperatorAsync(otherSeed, otherEmail);
        using var foreign = await other.SendAsync(HttpMethod.Get, originalOperation);
        await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "BillingDocument.OperationNotFound");
        using var foreignReplay = await UploadAsync(other, path, command);
        await AssertProblemAsync(foreignReplay, HttpStatusCode.Conflict, "BillingDocument.PayloadMismatch");

        command["issuerReference"] = "FIXTURE-CHANGED";
        using var mismatch = await UploadAsync(billing, path, command);
        await AssertProblemAsync(mismatch, HttpStatusCode.Conflict, "BillingDocument.PayloadMismatch");
        Assert.Equal(1L, await fixture.CountAsync("billing_document_versions"));
    }

    private static async Task AssertConcurrentResultsAsync(P0110CookieBrowser billing, CheckoutQuoteV1 quote, bool sameKey)
    {
        var path = Path(quote);
        var metadata = Metadata(Pdf());
        var digest = Digest(await PreviewAsync(billing, path, metadata));
        var first = Command(metadata, digest);
        var second = sameKey ? first : Command(metadata, digest);
        var proof = await P0206Fixture.ProofAsync(billing);
        var replies = await Task.WhenAll(UploadAsync(billing, path, first, proof: proof), UploadAsync(billing, path, second, proof: proof));
        using var reply1 = replies[0];
        using var reply2 = replies[1];
        var created = Assert.Single(replies, response => response.StatusCode == HttpStatusCode.Created);
        var other = Assert.Single(replies, response => response != created);
        var receipt = await ReadAsync(created, HttpStatusCode.Created);
        if (sameKey)
            Assert.Equal(receipt.GetRawText(), (await ReadAsync(other, HttpStatusCode.OK)).GetRawText());
        else
            await AssertProblemAsync(other, HttpStatusCode.Conflict, "BillingDocument.AlreadyAttached");
    }
}
