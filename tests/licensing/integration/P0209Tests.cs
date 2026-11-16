using System.Net;
using System.Text.Json;
using Intelibill.Domain.Enums;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>P02-09: one computed access basis, exact zoned deadlines and observe-only status for merchant and provider.</summary>
[Collection(MerchantBillingHosts.Name)]
public sealed partial class P0209Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    // 31 Jan 2027 10:00 IST: the month anchor whose February and March boundaries clip and then recover.
    private static readonly DateTimeOffset Jan31 = Utc("2027-01-31T04:30:00Z");
    private static readonly DateTimeOffset Feb28 = Utc("2027-02-28T04:30:00Z");

    [Fact]
    public async Task StatusReadsFrozenTermFactsAndRetainsCalendarAnchor()
    {
        await using var host = await StartAsync(database, Utc("2027-02-10T00:00:00Z"));
        var payer = await SignInAsync(host);
        var account = await CreateAccountAsync(payer.Client);
        var legacyOwner = await SignInAsync(host, ShopRole.Owner);
        var legacy = await SeedTermAsync(host, account, new TermSeed(Jan31, Feb28, Basis: null));
        await AffiliateAsync(host, account, payer.UserId, legacyOwner.ShopId, legacyOwner.UserId, legacy, Jan31, Feb28);

        // Coverage without its supplement: known dates and capacity, nothing invented.
        var incomplete = await ReadAsync(await ShopStatusAsync(legacyOwner.Client, legacyOwner.ShopId));
        Assert.Equal("EvidenceIncomplete", incomplete.GetProperty("state").GetString());
        var legacyTerm = incomplete.GetProperty("term");
        Assert.Equal("Incomplete", legacyTerm.GetProperty("evidenceState").GetString());
        Assert.Equal(JsonValueKind.Null, legacyTerm.GetProperty("basis").ValueKind);
        Assert.Equal(JsonValueKind.Null, legacyTerm.GetProperty("graceEndsAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, legacyTerm.GetProperty("memberAllowance").ValueKind);
        Assert.Equal(Feb28, legacyTerm.GetProperty("paidThrough").GetDateTimeOffset());
        Assert.Equal(1, legacyTerm.GetProperty("purchasedCapacity").GetInt32());

        var owner = await SignInAsync(host, ShopRole.Owner);
        var term = await SeedTermAsync(host, account, new TermSeed(Jan31, Feb28, AnchorLocal: "2027-01-31 10:00:00"));
        await AffiliateAsync(host, account, payer.UserId, owner.ShopId, owner.UserId, term, Jan31, Feb28);
        var status = await ReadAsync(await ShopStatusAsync(owner.Client, owner.ShopId));

        Assert.Equal("Active", status.GetProperty("state").GetString());
        Assert.True(status.GetProperty("observeOnly").GetBoolean());
        Assert.Equal("NotAvailable", status.GetProperty("entitlementSynchronization").GetString());
        Assert.Equal(Utc("2027-02-10T00:00:00Z"), status.GetProperty("evaluatedAt").GetDateTimeOffset());
        var paid = status.GetProperty("term");
        Assert.Equal("Complete", paid.GetProperty("evidenceState").GetString());
        Assert.Equal("Paid", paid.GetProperty("basis").GetString());
        Assert.Equal("Asia/Kolkata", paid.GetProperty("timeZone").GetString());
        Assert.Equal(Jan31, paid.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(Feb28, paid.GetProperty("paidThrough").GetDateTimeOffset());
        Assert.Equal(Utc("2027-03-07T04:30:00Z"), paid.GetProperty("graceEndsAt").GetDateTimeOffset());
        Assert.Equal(Feb28, paid.GetProperty("nextDeadline").GetDateTimeOffset());
        Assert.Equal(Utc("2027-03-31T04:30:00Z"), paid.GetProperty("nextPeriodEndsAt").GetDateTimeOffset());
        Assert.Equal(5, paid.GetProperty("memberAllowance").GetInt32());
        // A shop owner without billing membership sees no commercial facts or account identity.
        Assert.False(paid.TryGetProperty("commercial", out var commercial) && commercial.ValueKind != JsonValueKind.Null);
        Assert.False(status.TryGetProperty("accountId", out var accountId) && accountId.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task ShopStatusRefusesAnotherShopAndStaysNoStore()
    {
        await using var host = await StartAsync(database, Utc("2027-02-10T00:00:00Z"));
        var owner = await SignInAsync(host, ShopRole.Owner);
        var stranger = await SignInAsync(host, ShopRole.Owner);

        using var foreign = await ShopStatusAsync(stranger.Client, owner.ShopId);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        using var own = await ShopStatusAsync(owner.Client, owner.ShopId);
        var pending = await ReadAsync(own);
        Assert.Equal("PendingActivation", pending.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("term").ValueKind);
        Assert.Contains("no-store", own.Headers.CacheControl?.ToString() ?? string.Empty);
    }
}
