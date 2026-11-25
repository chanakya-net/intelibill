using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0607aTests
{
    private const string SourcePurpose = "source-billing-owner";
    private const string DestinationPurpose = "destination-billing-owner";

    [Fact]
    public async Task BillingConsentsRequireTheirOwnLiveAuthority()
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        var before = await s.BaselineAsync();
        var id = (await CreateAsync(s)).GetProperty("transferId").GetGuid();
        var (source, destination) = (s.Source.Payer.Client, s.Destination.Payer.Client);
        var sourcePath = ConsentPath(s.Source.Account, id, SourcePurpose);
        var shown = await ReadAsync(source, s.Source.Account, id);
        Assert.Equal(["SourceBillingOwner"], shown.GetProperty("viewerPurposes").EnumerateArray().Select(p => p.GetString()));

        // Nobody can give a purpose they do not hold: other party, wrong account route, outsider.
        foreach (var (client, path) in new[]
        {
            (destination, sourcePath), (source, ConsentPath(s.Source.Account, id, DestinationPurpose)),
            (source, ConsentPath(s.Destination.Account, id, DestinationPurpose)), (s.Source.Shops[1].Client, sourcePath),
            (s.North.Client, sourcePath),
        })
        {
            using var denied = await DecideAsync(client, path, "accept", shown);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        // A decision must bind the displayed terms and the owner's own membership revision.
        using var staleTerms = await DecideAsync(source, sourcePath, "accept", shown, termsDigest: new string('0', 64));
        using var staleRole = await DecideAsync(source, sourcePath, "accept", shown, authorityRevision: 99);
        Assert.Equal("Coverage.TransferChanged", await CodeAsync(staleTerms));
        Assert.Equal("Coverage.TransferChanged", await CodeAsync(staleRole));
        Assert.Equal(0L, await s.F.CountAsync("coverage_transfer_consents"));

        // Partial consent: the source alone. An identical retry returns the receipt; a second fresh key is refused.
        var key = Guid.NewGuid();
        using var accepted = await DecideAsync(source, sourcePath, "accept", shown, key);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var partial = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("Accepted", "AwaitingConsent", "AwaitingConsent"),
            (Status(partial, "SourceBillingOwner"), Status(partial, "DestinationBillingOwner"), Status(partial, "ShopOwner")));
        Assert.Equal("AwaitingConsents", partial.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, partial.GetProperty("destinationAccountName").ValueKind);
        using var retried = await DecideAsync(source, sourcePath, "accept", shown, key);
        using var duplicate = await DecideAsync(source, sourcePath, "accept", shown);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal("Coverage.TransferConsentInvalid", await CodeAsync(duplicate));
        Assert.Equal(1L, await s.F.CountAsync("coverage_transfer_consents"));
        Assert.Equal(before, await s.BaselineAsync());

        // The destination accepts its own purpose; only then is it named to the source. Still no shop-owner consent.
        var both = await AcceptAsync(destination, s.Destination.Account, id, DestinationPurpose);
        Assert.False(both.GetProperty("consentsCollected").GetBoolean());
        Assert.Equal("Destination Company",
            (await ReadAsync(source, s.Source.Account, id)).GetProperty("destinationAccountName").GetString());

        // Revocation is recorded, never erased, and a fresh acceptance starts a new generation.
        using var revoked = await DecideAsync(source, sourcePath, "revoke", await ReadAsync(source, s.Source.Account, id));
        Assert.Equal("Revoked", Status(await revoked.Content.ReadFromJsonAsync<JsonElement>(), "SourceBillingOwner"));
        var again = await AcceptAsync(source, s.Source.Account, id, SourcePurpose);
        Assert.Equal(2, again.GetProperty("consents")[0].GetProperty("generation").GetInt32());
        Assert.Equal(4L, await s.F.CountAsync("coverage_transfer_consents"));

        await AssertLossAndRegainNeedsFreshConsentAsync(s, id, key, shown);
        Assert.Equal(before, await s.BaselineAsync());
        Assert.Equal("Allowed", await P0606Tests.FencedTradingAtAsync(s.F, s.North.ShopId, RequestedAt));
    }

    // Ownership moves away and back: same person, same role, new membership revision. Old consent and old keys are dead.
    private static async Task AssertLossAndRegainNeedsFreshConsentAsync(Scenario s, Guid id, Guid oldKey, JsonElement oldShown)
    {
        var (owner, other) = (s.Source.Payer, s.Source.Shops[1]);
        var path = ConsentPath(s.Source.Account, id, SourcePurpose);
        await SwapBillingOwnerAsync(s, s.Source.Account, owner.UserId, other.UserId);
        Assert.Equal("Stale", Status(await ReadAsync(s.Destination.Payer.Client, s.Destination.Account, id), "SourceBillingOwner"));
        using var demotedReplay = await DecideAsync(owner.Client, path, "accept", oldShown, oldKey);
        using var demotedRead = await owner.Client.GetAsync(TransferPath(s.Source.Account, id));
        Assert.Equal(HttpStatusCode.Forbidden, demotedReplay.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, demotedRead.StatusCode);

        await SwapBillingOwnerAsync(s, s.Source.Account, other.UserId, owner.UserId);
        var regained = await ReadAsync(owner.Client, s.Source.Account, id);
        Assert.Equal("Stale", Status(regained, "SourceBillingOwner"));
        Assert.False(regained.GetProperty("consentsCollected").GetBoolean());
        using var replay = await DecideAsync(owner.Client, path, "accept", oldShown, oldKey);
        using var recovery = await owner.Client.GetAsync($"{path}/operations/{oldKey}");
        using var oldRevision = await DecideAsync(owner.Client, path, "accept", oldShown);
        Assert.Equal("Coverage.TransferAuthorityChanged", await CodeAsync(replay));
        Assert.Equal("Coverage.TransferAuthorityChanged", await CodeAsync(recovery));
        Assert.Equal("Coverage.TransferChanged", await CodeAsync(oldRevision));

        var fresh = await AcceptAsync(owner.Client, s.Source.Account, id, SourcePurpose);
        Assert.Equal("Accepted", Status(fresh, "SourceBillingOwner"));
        Assert.Equal(3, fresh.GetProperty("consents")[0].GetProperty("generation").GetInt32());
        // Every earlier decision is still there, unchanged.
        Assert.Equal(5L, await s.F.CountAsync("coverage_transfer_consents", $"transfer_id='{id}'"));
    }

    [Fact]
    public async Task OneOwnerOfBothAccountsStillDecidesEachPurposeSeparately()
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa, sameOwner: true);
        var before = await s.BaselineAsync();
        var owner = s.Source.Payer.Client;
        var id = (await CreateAsync(s)).GetProperty("transferId").GetGuid();

        var asSource = await AcceptAsync(owner, s.Source.Account, id, SourcePurpose);
        Assert.Equal(("Accepted", "AwaitingConsent"),
            (Status(asSource, "SourceBillingOwner"), Status(asSource, "DestinationBillingOwner")));
        var asDestination = await AcceptAsync(owner, s.Destination.Account, id, DestinationPurpose);
        Assert.Equal(("Accepted", "Accepted", "AwaitingConsent"), (Status(asDestination, "SourceBillingOwner"),
            Status(asDestination, "DestinationBillingOwner"), Status(asDestination, "ShopOwner")));
        Assert.Equal(2L, await s.F.CountAsync("coverage_transfer_consents", "operation_key IS NOT NULL"));
        // An empty destination froze no destination subscription evidence, and nothing moved.
        Assert.Equal(0L, await s.F.CountAsync("coverage_transfer_evidence", "side='Destination'"));
        Assert.Equal(before, await s.BaselineAsync());
    }
}
