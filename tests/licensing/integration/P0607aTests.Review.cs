using System.Net.Http.Json;
using System.Security.Cryptography;
using Intelibill.Licensing.Application.Coverage;
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
        Assert.Equal((Outcome.Stale, "ProposalChanged"), Result(await CheckAsync(s, id, revision + 1, digest)));
        Assert.Equal((Outcome.Stale, "ProposalChanged"), Result(await CheckAsync(s, id, revision, new string('0', 64))));

        await AssertShopOwnerEpochIsCheckedLiveAsync(s, id, Check);
        await AssertBillingOwnerEpochsAreCheckedAsync(s, id, Check);
        // Checking authority never moves coverage, money or access.
        Assert.Equal(before, await s.BaselineAsync());
        Assert.Equal("Allowed", await P0606Tests.FencedTradingAtAsync(s.F, s.North.ShopId, RequestedAt));

        await AssertWithheldPermitAndExpiryFailClosedAsync(s, id, Check);
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
