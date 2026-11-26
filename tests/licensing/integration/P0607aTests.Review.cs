using System.Net.Http.Json;
using System.Security.Cryptography;
using Intelibill.Licensing.Application.Coverage;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Outcome = Intelibill.Licensing.Application.Coverage.CoverageTransferReviewOutcome;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0607aTests
{
    [Fact]
    public async Task LiveReviewCheckRejectsLostAndRegainedAuthority()
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        var before = await s.BaselineAsync();
        var created = await CreateAsync(s);
        var id = created.GetProperty("transferId").GetGuid();
        var (revision, digest) = (created.GetProperty("proposalRevision").GetInt64(), created.GetProperty("termsDigest").GetString()!);
        Task<CoverageTransferReviewCheck> Check() => CheckAsync(s, id, revision, digest);

        // A request, and then partial consent, is never reviewable.
        Assert.Equal((Outcome.Incomplete, "SourceBillingOwnerAwaitingConsent"), Result(await Check()));
        await AcceptAsync(s.Source.Payer.Client, s.Source.Account, id, SourcePurpose);
        await AcceptAsync(s.Destination.Payer.Client, s.Destination.Account, id, DestinationPurpose);
        Assert.Equal((Outcome.Incomplete, "ShopOwnerAwaitingConsent"), Result(await Check()));

        await ShopAcceptAsync(s, id);
        var current = await Check();
        Assert.Equal(Outcome.Current, current.Outcome);
        Assert.Equal(["SourceBillingOwner", "DestinationBillingOwner", "ShopOwner"], current.Observation!.Consents.Select(c => c.Purpose));
        Assert.Equal(1, current.Observation.MerchantPermitCount);
        Assert.Equal(s.Permit.ValidUntil, current.Observation.MerchantMaxPermitValidUntil);
        Assert.True(EntitlementSnapshotsV1.IsDigest(current.Observation.MerchantPermitDigest));
        Assert.Equal((Outcome.Stale, "ProposalChanged"), Result(await CheckAsync(s, id, revision + 1, digest)));
        Assert.Equal((Outcome.Stale, "ProposalChanged"), Result(await CheckAsync(s, id, revision, new string('0', 64))));

        await AssertShopOwnerEpochIsCheckedLiveAsync(s, id, Check);
        await AssertBillingOwnerEpochsAreCheckedAsync(s, id, Check);
        // Checking authority never moves coverage, money or access.
        Assert.Equal(before, await s.BaselineAsync());
        Assert.Equal("Allowed", await P0606Tests.FencedTradingAtAsync(s.F, s.North.ShopId, RequestedAt));

        await AssertWithheldPermitAndExpiryFailClosedAsync(s, id, Check);
    }

    [Fact]
    public async Task RemainingPermitIdentityMustMatchAfterAnObservedPermitExpires()
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        // Enter the scheduled entitlement refresh window while the first permit is still active.
        var issuedAt = s.Permit.ValidUntil.AddMinutes(-10);
        var longer = await P0606Tests.IssueRealPermitAsync(s.F, s.North, issuedAt, true, "Second till");
        Assert.True(s.Permit.ValidUntil < longer.ValidUntil, $"First {s.Permit.ValidUntil:O}, second {longer.ValidUntil:O} ({longer.Reason})");
        var proposal = await CollectConsentsAsync(s);
        Assert.Equal(2, proposal.ObservedPermitCount);
        Task<CoverageTransferReviewCheck> Check() => CheckAsync(s, proposal.TransferId, proposal.ProposalRevision, proposal.TermsDigest);
        Assert.Equal(Outcome.Current, (await Check()).Outcome);

        var inTransit = await P0606Tests.IssueRealPermitAsync(s.F, s.North, issuedAt, false, "Third till");
        s.F.At(s.Permit.ValidUntil.AddTicks(10));
        Assert.InRange(inTransit.ValidUntil, s.F.Checkout.CheckoutClock.Now.AddTicks(10), longer.ValidUntil);
        Assert.Equal((Outcome.Stale, "PermitEvidenceChanged"), Result(await Check()));
    }

    [Fact]
    public async Task ReviewRequiresAStillVerifiedShopOwner()
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        var proposal = await CollectConsentsAsync(s);
        Task<CoverageTransferReviewCheck> Check() => CheckAsync(s, proposal.TransferId, proposal.ProposalRevision, proposal.TermsDigest);
        Assert.Equal(Outcome.Current, (await Check()).Outcome);
        await LicensingPostgreSqlFixture.ExecuteAsync(s.F.Checkout.Delivery.MerchantDb, $"""
            UPDATE users SET is_email_verified = false, is_phone_verified = false WHERE id = '{s.North.UserId}'
            """);
        Assert.Equal((Outcome.Stale, "ShopOwnerChanged"), Result(await Check()));
    }

    [Fact]
    public async Task InternalAuthorityCheckValidatesInputAndConsumesProofOnce()
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        var clock = s.F.Hosts.Fixture.Clock;
        var check = new CoverageTransferShopAuthorityCheckV1(1, Guid.NewGuid(), Guid.NewGuid(), s.Source.Account,
            s.Destination.Account, s.North.ShopId, Guid.NewGuid(), 1, new string('a', 64), Guid.NewGuid(),
            s.North.UserId, Guid.NewGuid(), 1, EntitlementSnapshotsV1.Instant(clock).AddSeconds(20));
        const string path = CoverageTransfersV1.ShopAuthorityPath;
        var transport = s.F.Checkout.Delivery.Transport;
        using var api = s.F.Hosts.Merchant.CreateClient();
        var invalid = JsonSerializer.SerializeToUtf8Bytes(check with { Challenge = Guid.Empty }, EntitlementSnapshotsV1.Json);
        using var invalidRequest = await transport.SignedAsync(invalid, clock, path: path);
        using var invalidResponse = await api.SendAsync(invalidRequest);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        var body = JsonSerializer.SerializeToUtf8Bytes(check, EntitlementSnapshotsV1.Json);
        using var first = await transport.SignedAsync(body, clock, path: path);
        using var duplicate = P0213Transport.Request(body, path, first.Headers.Authorization!.Parameter);
        using var receipt = await api.SendAsync(first);
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        Assert.False((await receipt.Content.ReadFromJsonAsync<CoverageTransferShopAuthorityV1>())!.Current);
        using var repeated = await api.SendAsync(duplicate);
        Assert.Equal(HttpStatusCode.Unauthorized, repeated.StatusCode);
    }

    [Fact]
    public async Task FinalReviewPredicateRequiresAnOpenTransaction()
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        var transfer = await CollectConsentsAsync(s);
        await using var scope = s.F.Hosts.Licensing.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<ICoverageTransferReviewStore>();
        Task<CoverageTransferReviewCheck> Evaluate() => store.EvaluateInTransactionAsync(
            transfer.TransferId, transfer.ProposalRevision, transfer.TermsDigest, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(Evaluate);
        await using var tx = await services.GetRequiredService<LicensingDbContext>().Database.BeginTransactionAsync();
        Assert.Equal(Outcome.Current, (await Evaluate()).Outcome);
        await tx.CommitAsync();
    }

    [Fact]
    public async Task ReviewFailsClosedWhenTheExchangeCannotConfirmAuthority()
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        var proposal = await CollectConsentsAsync(s);
        Task<CoverageTransferReviewCheck> Check() => CheckAsync(s, proposal.TransferId, proposal.ProposalRevision, proposal.TermsDigest);
        var transport = s.F.Checkout.Delivery.Transport;
        var clock = s.F.Hosts.Fixture.Clock;
        var original = clock.Now;
        foreach (var fault in new[] { "unavailable", "unsigned", "wrong challenge", "invalid response", "deadline" })
        {
            transport.AfterCommit = async (request, response, ct) =>
            {
                Assert.Equal(CoverageTransfersV1.ShopAuthorityPath, request.RequestUri!.AbsolutePath);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                switch (fault)
                {
                    case "unavailable": response.StatusCode = HttpStatusCode.ServiceUnavailable; break;
                    case "unsigned": response.Headers.Remove(CoverageTransfersV1.ProofHeader); break;
                    case "deadline": clock.Now = original.AddSeconds(30); break;
                    default:
                        var answer = (await response.Content.ReadFromJsonAsync<CoverageTransferShopAuthorityV1>(ct))!;
                        await SetFixtureReplyAsync(transport, clock, response, fault == "wrong challenge"
                            ? answer with { Challenge = Guid.NewGuid() } : answer with { OutstandingPermitCount = -1 });
                        break;
                }
            };
            Assert.Equal((Outcome.Unavailable, "ShopAuthorityUnavailable"), Result(await Check()));
            clock.Now = original;
        }
        transport.AfterCommit = null;
        Assert.Equal(Outcome.Current, (await Check()).Outcome);
    }

    private static async Task SetFixtureReplyAsync(P0213Transport transport, TimeProvider clock,
        HttpResponseMessage response, CoverageTransferShopAuthorityV1 answer)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(answer, EntitlementSnapshotsV1.Json);
        using var signed = await transport.SignedAsync(bytes, clock, merchant: true, path: CoverageTransfersV1.ShopAuthorityPath);
        response.Headers.Remove(CoverageTransfersV1.ProofHeader);
        response.Headers.Add(CoverageTransfersV1.ProofHeader, signed.Headers.Authorization!.Parameter);
        response.Content.Dispose();
        response.Content = new ByteArrayContent(bytes);
        response.Content.Headers.ContentType = new("application/json");
    }

    [Fact]
    public async Task BillingOwnerChangeDuringExchangeIsRecheckedUnderLock()
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        var proposal = await CollectConsentsAsync(s);
        var transport = s.F.Checkout.Delivery.Transport;
        transport.AfterCommit = async (request, response, _) =>
        {
            Assert.Equal(CoverageTransfersV1.ShopAuthorityPath, request.RequestUri!.AbsolutePath);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await SwapBillingOwnerAsync(s, s.Destination.Account, s.Destination.Payer.UserId, s.Source.Shops[1].UserId);
        };
        var check = await CheckAsync(s, proposal.TransferId, proposal.ProposalRevision, proposal.TermsDigest);
        Assert.Equal((Outcome.Stale, "DestinationBillingOwnerAuthorityChanged"), Result(check));
    }

    private static async Task<CoverageTransferV1> CollectConsentsAsync(Scenario s)
    {
        var proposal = (await CreateAsync(s)).Deserialize<CoverageTransferV1>(EntitlementSnapshotsV1.Json)!;
        await AcceptAsync(s.Source.Payer.Client, s.Source.Account, proposal.TransferId, SourcePurpose);
        await AcceptAsync(s.Destination.Payer.Client, s.Destination.Account, proposal.TransferId, DestinationPurpose);
        await ShopAcceptAsync(s, proposal.TransferId);
        return proposal;
    }

    // Merchant ownership moves away and back. Licensing still displays collected consents, yet the live check refuses.
    private static async Task AssertShopOwnerEpochIsCheckedLiveAsync(Scenario s, Guid id, Func<Task<CoverageTransferReviewCheck>> check)
    {
        var merchant = s.F.Hosts.Database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase);
        Task SetRoleAsync(string role) => LicensingPostgreSqlFixture.ExecuteAsync(merchant, $"""
            UPDATE public.shop_memberships SET role = '{role}', authority_version = authority_version + 1
            WHERE shop_id = '{s.North.ShopId}' AND user_id = '{s.North.UserId}'
            """);
        await SetRoleAsync("Manager");
        Assert.True((await ReadAsync(s.Source.Payer.Client, s.Source.Account, id)).GetProperty("consentsCollected").GetBoolean());
        Assert.Equal((Outcome.Stale, "ShopOwnerChanged"), Result(await check()));
        await SetRoleAsync("Owner");
        Assert.Equal((Outcome.Stale, "ShopOwnerChanged"), Result(await check()));

        // The regained owner signs in again and gives a fresh consent for the same displayed terms.
        await P0606Tests.SignInAtAsync(s.North);
        var shop = await ShopAcceptAsync(s, id);
        Assert.Equal("Accepted", shop.GetProperty("consentStatus").GetString());
        Assert.Equal(Outcome.Current, (await check()).Outcome);

        // A revocation still waiting in the merchant outbox is already decisive at the merchant.
        var path = ShopConsentPath(s.Source.Account, s.North.ShopId, id);
        using var revoked = await DecideAsync(s.North.Client, path, "revoke", await s.North.Client.GetFromJsonAsync<System.Text.Json.JsonElement>(path));
        Assert.True(revoked.IsSuccessStatusCode);
        Assert.Equal((Outcome.Stale, "ShopConsentSuperseded"), Result(await check()));
        await s.F.Checkout.Delivery.DrainAsync(false);
        Assert.Equal((Outcome.Incomplete, "ShopOwnerRevoked"), Result(await check()));
        await ShopAcceptAsync(s, id);
        Assert.Equal(Outcome.Current, (await check()).Outcome);
    }

    private static async Task AssertBillingOwnerEpochsAreCheckedAsync(Scenario s, Guid id, Func<Task<CoverageTransferReviewCheck>> check)
    {
        foreach (var (party, purpose, segment) in new[]
        {
            (s.Source, "SourceBillingOwner", SourcePurpose), (s.Destination, "DestinationBillingOwner", DestinationPurpose),
        })
        {
            var stranger = s.Source.Shops[3].UserId;
            await SwapBillingOwnerAsync(s, party.Account, party.Payer.UserId, stranger);
            Assert.Equal((Outcome.Stale, purpose + "AuthorityChanged"), Result(await check()));
            await SwapBillingOwnerAsync(s, party.Account, stranger, party.Payer.UserId);
            Assert.Equal((Outcome.Stale, purpose + "AuthorityChanged"), Result(await check()));
            await AcceptAsync(party.Payer.Client, party.Account, id, segment);
            Assert.Equal(Outcome.Current, (await check()).Outcome);
        }
    }

    private static async Task AssertWithheldPermitAndExpiryFailClosedAsync(Scenario s, Guid id, Func<Task<CoverageTransferReviewCheck>> check)
    {
        // A second permit exists at the merchant but its observation never reached licensing. It may share the first
        // permit's deadline, so the merchant's actual permit count is what exposes it.
        var later = await P0606Tests.IssueRealPermitAsync(s.F, s.North, RequestedAt.AddHours(1), deliverObservation: false,
            device: "North second till");
        Assert.True(later.ValidUntil >= s.Permit.ValidUntil);
        s.F.At(RequestedAt.AddHours(1));
        Assert.Equal((Outcome.Stale, "PermitEvidenceChanged"), Result(await check()));

        // Half-open lifetime. By then every permit has itself expired, so the proposal is reviewable again one
        // microsecond before its expiry and gone exactly at it.
        var expires = (DateTime)(await s.F.ScalarAsync($"SELECT expires_at FROM coverage_transfer_proposals WHERE transfer_id='{id}'"))!;
        var expiresAt = new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc));
        s.F.At(expiresAt.AddTicks(-10));
        Assert.Equal(Outcome.Current, (await check()).Outcome);
        s.F.At(expiresAt);
        Assert.Equal((Outcome.Expired, "Expired"), Result(await check()));
        Assert.Equal("Expired", (await ReadAsync(s.Source.Payer.Client, s.Source.Account, id)).GetProperty("state").GetString());
        Assert.Equal(0L, await s.F.CountAsync("paid_slot_handoffs"));
    }

    private static (Outcome, string?) Result(CoverageTransferReviewCheck check) => (check.Outcome, check.Reason);

    /// <summary>The production evaluator P06-07 will call, resolved from the licensing host exactly as registered.</summary>
    internal static async Task<CoverageTransferReviewCheck> CheckAsync(Scenario s, Guid id, long revision, string digest)
    {
        await using var scope = s.F.Hosts.Licensing.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CoverageTransferAuthority>()
            .CheckForReviewAsync(id, revision, digest, CancellationToken.None);
    }
}
