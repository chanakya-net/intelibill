using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Grants;

/// <summary>
/// The facts a grant is judged against, read under the account fence at one evaluation instant: the account's grant
/// revision, each named shop's affiliation identity and current status, and existing windows. Its hash is a
/// consistency token binding a confirmation to the preview it was made from; it is never authorization.
/// </summary>
internal sealed record ComplimentaryGrantSource(long Revision, IReadOnlyList<GrantShopSource> Shops,
    IReadOnlyList<ComplimentaryGrantShopV1> Baselines, string Hash, DateTimeOffset At, Guid? PaidTermId = null)
{
    internal static async Task<ErrorOr<ComplimentaryGrantSource>> ReadAsync(IComplimentaryGrantStore store,
        ISubscriptionAccessFactsReader reader, Guid account, ComplimentaryGrantFacts facts, DateTimeOffset at,
        CancellationToken ct)
    {
        var shops = await store.ReadShopsAsync(account, facts.ShopIds, ct);
        if (shops.Count != facts.ShopIds.Length) return ComplimentaryGrantErrors.ShopNotAffiliated;
        if (facts.EndsAt <= at) return ComplimentaryGrantErrors.AlreadyEnded;
        var windows = await store.ReadWindowsAsync(account, facts.ShopIds, ct);
        if (windows.Any(window => ComplimentaryAccessPolicy.Overlaps(facts.StartsAt, facts.EndsAt, window.StartsAt, window.EndsAt)))
            return ComplimentaryGrantErrors.Overlap;

        Guid? paidTerm = null;
        if (facts.PaymentOrderId is { } order)
        {
            paidTerm = await store.ReadPaidTermAsync(account, order, facts.ShopIds, ct);
            if (paidTerm is null) return ComplimentaryGrantErrors.InvalidPaymentOrder;
        }

        var revision = await store.ReadRevisionAsync(account, ct);
        var baselines = new List<ComplimentaryGrantShopV1>();
        var terms = new List<Guid?>();
        foreach (var shop in shops)
        {
            var coverage = await reader.ReadShopAsync(shop.ShopId, ct);
            var status = SubscriptionStatusProjection.Shop(shop.ShopId, at, coverage, commercial: false);
            baselines.Add(new(shop.ShopId, shop.ShopName, status.State, status.Term?.Basis, status.Term?.AccessEndsAt));
            terms.Add(coverage?.Term?.TermId);
        }
        var hash = SourceHash(account, revision, shops, windows, terms);
        if (paidTerm is not null)
            hash = CheckoutOrdersV1.Digest("service-restoration/1", "source", hash, facts.PaymentOrderId, paidTerm);
        return new ComplimentaryGrantSource(revision, shops, baselines, hash, at, paidTerm);
    }

    // Stable facts only (identities and revision), never the time-dependent evaluated state.
    private static string SourceHash(Guid account, long revision, IReadOnlyList<GrantShopSource> shops,
        IReadOnlyList<GrantWindow> windows, List<Guid?> baselineTerms)
    {
        var canonical = JsonSerializer.Serialize(new object[]
        {
            "complimentary-grant-source/1", account, revision,
            shops.Select((shop, index) => new object?[] { shop.ShopId, shop.AffiliationRequestId, baselineTerms[index] }),
            windows.OrderBy(window => window.ShopId).ThenBy(window => window.GrantId)
                .Select(window => new object[] { window.ShopId, window.GrantId }),
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    internal static string Digest(Guid account, Guid operationKey, ComplimentaryGrantFacts facts, long expectedVersion,
        string sourceHash)
    {
        var canonical = JsonSerializer.Serialize(new object[]
        {
            "complimentary-grant/1", account, operationKey, facts.ShopIds,
            ComplimentaryGrantFacts.Format(facts.StartsAt), ComplimentaryGrantFacts.Format(facts.EndsAt),
            facts.MemberAllowance, facts.Reason, expectedVersion, sourceHash,
        });
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return facts.PaymentOrderId is { } order
            ? CheckoutOrdersV1.Digest("service-restoration/1", "grant", digest, order) : digest;
    }

    internal ComplimentaryGrantPreviewV1 Preview(Guid account, ComplimentaryGrantFacts facts) =>
        new(ComplimentaryGrantFacts.SchemaVersion, account, ComplimentaryGrantFacts.Basis, facts.ShopIds, [.. Baselines],
            ComplimentaryGrantFacts.Format(facts.StartsAt), ComplimentaryGrantFacts.Format(facts.EndsAt),
            ComplimentaryGrantFacts.TimeZone, facts.CoverageMode, facts.ShopIds.Length, facts.MemberAllowance, facts.Reason,
            Revision, Hash, At, PaymentRecorded: false, GraceEndsAt: null, ComplimentaryAccessPolicy.Version, facts.PaymentOrderId);

    internal static ComplimentaryGrantResultV1 Result(StoredComplimentaryGrant grant) =>
        new(ComplimentaryGrantFacts.SchemaVersion, grant.OperationKey, grant.GrantId, grant.TermId, grant.AccountId,
            ComplimentaryGrantFacts.Basis, grant.Facts.ShopIds, ComplimentaryGrantFacts.Format(grant.Facts.StartsAt),
            ComplimentaryGrantFacts.Format(grant.Facts.EndsAt), ComplimentaryGrantFacts.TimeZone, grant.Facts.CoverageMode,
            grant.Facts.ShopIds.Length, grant.Facts.MemberAllowance, grant.Facts.Reason, grant.ExpectedVersion,
            grant.ExpectedVersion + 1, grant.SourceHash, grant.RecordedAt, PaymentRecorded: false, GraceEndsAt: null,
            ComplimentaryAccessPolicy.Version, grant.Facts.PaymentOrderId);

    internal static bool ValidHash(string? value) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
