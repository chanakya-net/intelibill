using System.Globalization;
using System.Text.Json;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0209Tests
{
    private static readonly Lazy<JsonElement> TimeV1 = new(() => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(ProjectPaths.Root, "tests/contracts/licensing/time-v1.json"))).RootElement.Clone());

    [Fact]
    public void AccessAndRenewalMatchTimeV1AtEveryBoundary()
    {
        var contract = TimeV1.Value;
        Assert.Equal(BillingCalendar.Zone, contract.GetProperty("zone").GetString());
        Assert.Equal(BillingCalendar.Version, contract.GetProperty("calendarVersion").GetString());
        Assert.Equal(GracePolicy.Version, contract.GetProperty("gracePolicyVersion").GetString());
        Assert.Equal(SubscriptionAccessPolicy.Version, contract.GetProperty("accessPolicyVersion").GetString());
        Assert.Equal(RenewalPolicy.Version, contract.GetProperty("renewalPolicyVersion").GetString());

        foreach (var row in contract.GetProperty("calendar").EnumerateArray())
        {
            var anchor = Anchor(row.GetProperty("anchorLocal"), row.GetProperty("period"));
            var expected = row.GetProperty("boundaries").EnumerateArray().Select(Instant).ToArray();
            var actual = Enumerable.Range(0, expected.Length).Select(ordinal => BillingCalendar.Boundary(anchor, ordinal)).ToArray();
            Assert.True(expected.SequenceEqual(actual), $"{row.GetProperty("name")}: {string.Join(", ", actual.Select(Format))}");
        }

        foreach (var row in contract.GetProperty("access").EnumerateArray())
            AssertAccess(contract, row);
        foreach (var row in contract.GetProperty("renewal").EnumerateArray())
            AssertRenewal(contract, row);
    }

    [Fact]
    public void CalendarRefusesUnsupportedZonesInsteadOfGuessing()
    {
        var utc = new BillingAnchor(new DateTime(2027, 1, 31, 10, 0, 0), "UTC", BillingPeriod.Month);
        Assert.False(BillingCalendar.IsSupported(utc));
        Assert.Throws<ArgumentException>(() => BillingCalendar.Boundary(utc, 1));
        var fractional = new BillingAnchor(new DateTime(2027, 1, 31, 10, 0, 0).AddTicks(10), BillingCalendar.Zone, BillingPeriod.Month);
        Assert.False(BillingCalendar.IsSupported(fractional));
        var noPrior = RenewalPolicy.DetermineTerm(new RenewalInput(BillingPeriod.Month, Jan31, SubscriptionBasis.Paid));
        Assert.Equal(RenewalStart.Unresolved, noPrior.Start);
    }

    [Fact]
    public void StatusContractMatchesTheSharedVersionedShapes()
    {
        var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectPaths.Root,
            "tests/contracts/licensing/subscription-status-v1.json"))).RootElement;
        var actions = contract.GetProperty("actions");
        var shop = Guid.NewGuid();
        var account = Guid.NewGuid();
        Assert.Equal(Shared.Contracts.V1.SubscriptionStatusV1.ShopRead, actions.GetProperty("shop").GetProperty("action").GetString());
        Assert.Equal(Shared.Contracts.V1.SubscriptionStatusV1.AccountRead, actions.GetProperty("account").GetProperty("action").GetString());
        Assert.True(Shared.Contracts.V1.MerchantBillingDelegationV1.IsActorScoped(Shared.Contracts.V1.SubscriptionStatusV1.ShopRead));
        Assert.False(Shared.Contracts.V1.MerchantBillingDelegationV1.IsActorScoped(Shared.Contracts.V1.SubscriptionStatusV1.AccountRead));
        Assert.Equal(actions.GetProperty("shop").GetProperty("path").GetString()!.Replace("{shopId}", shop.ToString("D"), StringComparison.Ordinal),
            Shared.Contracts.V1.SubscriptionStatusV1.ShopPath(shop));
        Assert.Equal(actions.GetProperty("account").GetProperty("continuation").GetString()!
            .Replace("{accountId}", account.ToString("D"), StringComparison.Ordinal).Replace("{termId}", shop.ToString("D"), StringComparison.Ordinal),
            Shared.Contracts.V1.SubscriptionStatusV1.AccountPath(account, shop));
        Assert.Equal(Shared.Contracts.V1.SubscriptionStatusV1.PageSize, contract.GetProperty("pageSize").GetInt32());
        Assert.Equal(Shared.Contracts.V1.SubscriptionStatusV1.States, contract.GetProperty("states").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(Enum.GetNames<SubscriptionAccessState>(), contract.GetProperty("states").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(Enum.GetNames<SubscriptionBasis>(), contract.GetProperty("bases").EnumerateArray().Select(x => x.GetString()));
        var example = contract.GetProperty("example").Deserialize<Shared.Contracts.V1.ShopSubscriptionStatusV1>()!;
        Assert.True(example.ObserveOnly);
        Assert.Equal("NotAvailable", example.EntitlementSynchronization);
    }

    private static void AssertAccess(JsonElement contract, JsonElement row)
    {
        var name = row.GetProperty("name").GetString();
        var term = contract.GetProperty("terms").GetProperty(row.GetProperty("term").GetString()!);
        var basis = row.TryGetProperty("basis", out var overridden) ? overridden : term.GetProperty("basis");
        var facts = basis.ValueKind == JsonValueKind.Null
            ? new SubscriptionAccessFacts(Instant(term.GetProperty("startsAt")), Instant(term.GetProperty("endsAt")), TermEvidenceState.Incomplete)
            : new SubscriptionAccessFacts(Instant(term.GetProperty("startsAt")), Instant(term.GetProperty("endsAt")),
                TermEvidenceState.Complete, Enum.Parse<SubscriptionBasis>(basis.GetString()!),
                Anchor(term.GetProperty("anchorLocal"), term.GetProperty("period")), term.GetProperty("ordinal").GetInt32(),
                row.TryGetProperty("cancellation", out var cancel) ? new ScheduledCancellation(Instant(cancel.GetProperty("effectiveAt")),
                    cancel.TryGetProperty("withdrawnAt", out var withdrawn) ? Instant(withdrawn) : null) : null,
                row.TryGetProperty("suspensions", out var suspensions) ? [.. suspensions.EnumerateArray().Select(item =>
                    new SuspensionInterval(Instant(item.GetProperty("startsAt")),
                        item.TryGetProperty("releasedAt", out var released) ? Instant(released) : null))] : null);

        var result = SubscriptionAccessPolicy.Evaluate(facts, Instant(row.GetProperty("at")));
        var expect = row.GetProperty("expect");
        Assert.True(expect.GetProperty("state").GetString() == result.State.ToString(), $"{name}: {result}");
        AssertOptional(expect, "nextDeadline", result.NextDeadline, name);
        AssertOptional(expect, "graceEndsAt", result.GraceEndsAt, name);
        AssertOptional(expect, "accessEndsAt", result.AccessEndsAt, name);
        AssertOptional(expect, "nextPeriodEndsAt", result.NextPeriodEndsAt, name);
    }

    private static void AssertRenewal(JsonElement contract, JsonElement row)
    {
        var name = row.GetProperty("name").GetString();
        var input = new RenewalInput(Enum.Parse<BillingPeriod>(row.GetProperty("period").GetString()!),
            Instant(row.GetProperty("fulfilledAt")),
            row.TryGetProperty("priorBasis", out var basis) ? Enum.Parse<SubscriptionBasis>(basis.GetString()!) : null,
            VerifiedPaymentAt: row.TryGetProperty("verifiedPaymentAt", out var verified) ? Instant(verified) : null,
            AcceptedQuote: row.TryGetProperty("acceptedQuote", out var quote) && quote.GetBoolean(),
            RestoredAccessAt: row.TryGetProperty("restoredAccessAt", out var restored) ? Instant(restored) : null);
        if (row.TryGetProperty("prior", out var priorName))
        {
            var prior = contract.GetProperty("priors").GetProperty(priorName.GetString()!);
            input = input with
            {
                PriorBasis = Enum.Parse<SubscriptionBasis>(prior.GetProperty("basis").GetString()!),
                PriorPaidThrough = Instant(prior.GetProperty("paidThrough")),
                PriorAnchor = Anchor(prior.GetProperty("anchorLocal"), prior.GetProperty("period")),
                PriorOrdinal = prior.GetProperty("ordinal").GetInt32(),
            };
        }

        var decision = RenewalPolicy.DetermineTerm(input);
        var expect = row.GetProperty("expect");
        Assert.True(expect.GetProperty("start").GetString() == decision.Start.ToString(), $"{name}: {decision}");
        AssertOptional(expect, "startsAt", decision.StartsAt, name);
        AssertOptional(expect, "endsAt", decision.EndsAt, name);
        if (expect.TryGetProperty("ordinal", out var ordinal)) Assert.True(ordinal.GetInt32() == decision.Ordinal, $"{name}: {decision}");
        if (expect.TryGetProperty("includedGraceDays", out var days))
            Assert.True(days.GetInt32() == decision.IncludedGraceDays, $"{name}: {decision}");
        if (expect.TryGetProperty("anchorLocal", out var local))
            Assert.True(LocalAnchor(local) == decision.Anchor?.LocalStart, $"{name}: {decision}");
    }

    private static void AssertOptional(JsonElement expect, string field, DateTimeOffset? actual, string? name)
    {
        if (!expect.TryGetProperty(field, out var value)) return;
        var expected = value.ValueKind == JsonValueKind.Null ? (DateTimeOffset?)null : Instant(value);
        Assert.True(expected == actual, $"{name}.{field}: expected {expected:O}, actual {actual:O}");
    }

    private static BillingAnchor Anchor(JsonElement local, JsonElement period) =>
        new(LocalAnchor(local), BillingCalendar.Zone, Enum.Parse<BillingPeriod>(period.GetString()!));

    private static DateTime LocalAnchor(JsonElement local) => DateTime.ParseExact(local.GetString()!, "yyyy-MM-dd'T'HH:mm:ss",
        CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static DateTimeOffset Instant(JsonElement value) => DateTimeOffset.Parse(value.GetString()!,
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static string Format(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
