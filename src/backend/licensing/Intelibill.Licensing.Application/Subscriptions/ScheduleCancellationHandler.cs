using System.Security.Cryptography;
using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Application.Entitlements;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Subscriptions;

public sealed class ScheduleCancellationHandler(IBillingDelegationAccess access, IScheduledSubscriptionChangeStore store,
    IEntitlementPublisher publisher, TimeProvider clock)
{
    public static Error Missing => Error.NotFound("Billing.CancellationNotFound", "Cancellation details are unavailable.");
    public static Error Conflict => Error.Conflict("Billing.CancellationConflict", "The term or affected shops changed. Review the current terms.");
    public static Error Invalid => Error.Validation("Billing.CancellationInvalid", "Confirm the current term end and affected shops.");

    public Task<ErrorOr<SubscriptionCancellationDetailV1>> ReadAsync(VerifiedBillingDelegation proof, Guid account,
        Guid term, Guid? after, CancellationToken ct) =>
        access.ExecuteAsync<SubscriptionCancellationDetailV1>(proof, (_, token) =>
            ReadDetailAsync(account, term, after, commercial: true, savedOnly: false, token), ct);

    public Task<ErrorOr<SubscriptionCancellationOutcomeV1>> ScheduleAsync(VerifiedBillingDelegation proof, Guid account,
        Guid term, ScheduleSubscriptionCancellationV1 request, CancellationToken ct) =>
        access.ExecuteAsync<SubscriptionCancellationOutcomeV1>(proof, (_, token) =>
            AcceptAsync(proof, account, term, request, token), ct);

    public Task<ErrorOr<SubscriptionCancellationOutcomeV1>> RecoverAsync(VerifiedBillingDelegation proof, Guid account,
        Guid term, Guid key, CancellationToken ct) =>
        access.ExecuteAsync<SubscriptionCancellationOutcomeV1>(proof, async (_, token) =>
        {
            var receipt = await store.FindReceiptAsync(account, key, token);
            return receipt is { Kind: CancellationReceipt.ScheduleKind } && receipt.ActorId == proof.ActorId
                && receipt.TermId == term ? receipt.Scheduled(account, key) : Missing;
        }, ct);

    public async Task<ErrorOr<SubscriptionCancellationDetailV1>> ReadDetailAsync(Guid account, Guid term, Guid? after,
        bool commercial, bool savedOnly, CancellationToken ct)
    {
        var row = await store.ReadTermAsync(account, term, ct);
        if (row is null) return Missing;
        if (SubscriptionStatusProjection.ActiveCancellation(row) is { } saved
            && SubscriptionStatusProjection.ToFacts(row).Evidence == TermEvidenceState.Complete)
        {
            var frozen = await store.ReadFrozenShopsAsync(account, saved.ChangeId, after, ct);
            return Detail(row, saved.Version, saved.ChangeId, commercial, null, saved.ShopHash, saved.ShopCount, frozen);
        }

        if (savedOnly) return Missing;
        var source = await ReadSourceAsync(row, SubscriptionStatusProjection.EvaluationInstant(clock), ct);
        if (source is null) return Conflict;
        var page = source.Shops.Where(shop => after is null || shop.ShopId.CompareTo(after.Value) > 0)
            .Take(SubscriptionCancellationsV1.PageSize + 1).Select(shop => new CancellationShopV1(shop.ShopId, shop.ShopName))
            .ToArray();
        return Detail(row, row.Cancellation?.Version ?? 0, null, commercial, source.SourceHash, source.ShopHash, source.Shops.Length, page);
    }

    private async Task<ErrorOr<SubscriptionCancellationOutcomeV1>> AcceptAsync(VerifiedBillingDelegation proof, Guid account,
        Guid term, ScheduleSubscriptionCancellationV1 request, CancellationToken ct)
    {
        if (!request.IsValid()) return Invalid;
        var digest = Hash(request);
        var receipt = await store.FindReceiptAsync(account, request.OperationKey, ct);
        if (receipt is not null)
            return receipt is { Kind: CancellationReceipt.ScheduleKind } && receipt.ActorId == proof.ActorId
                && receipt.TermId == term && receipt.PayloadHash == digest
                ? receipt.Scheduled(account, request.OperationKey) : BillingAccountErrors.PayloadMismatch;

        var row = await store.ReadTermAsync(account, term, ct);
        if (row is null) return Missing;
        var at = SubscriptionStatusProjection.EvaluationInstant(clock);
        var source = await ReadSourceAsync(row, at, ct);
        var current = row.Cancellation?.Version ?? 0;
        if (source is null || request.ExpectedVersion != current || request.AcceptedEffectiveAt != row.EndsAt
            || request.SourceHash != source.SourceHash || request.AffectedShopSetHash != source.ShopHash
            || request.AffectedShopCount != source.Shops.Length)
            return Conflict;

        var change = Guid.NewGuid();
        await store.AppendAsync(proof, source, change, current + 1, request.OperationKey, digest, at, ct);
        foreach (var shop in source.Shops)
            await publisher.PublishAsync(account, shop.ShopId, ct);
        return new SubscriptionCancellationOutcomeV1(1, account, term, request.OperationKey,
            new(change, current + 1, row.EndsAt, SubscriptionCancellationsV1.Policy), source.Shops.Length);
    }

    private async Task<CancellationSource?> ReadSourceAsync(TermFactsRow row, DateTimeOffset at, CancellationToken ct)
    {
        var facts = SubscriptionStatusProjection.ToFacts(row);
        if (row.RefundBoundary is not null || SubscriptionStatusProjection.ActiveCancellation(row) is not null || facts is not { Evidence: TermEvidenceState.Complete, Basis: SubscriptionBasis.Paid }
            || row.PricePaise is not >= 0 || row.Currency != "INR" || at < row.StartsAt || at >= row.EndsAt)
            return null;

        var shops = await store.ReadAssignedShopsAsync(row.AccountId, row.TermId, ct);
        if (shops.Length == 0 || shops.Length > row.Capacity
            || shops.Any(shop => shop.AppliedAt > at || shop.CoverageEndsAt != row.EndsAt
                || shop.ShopName.Length > SubscriptionCancellationsV1.MaximumShopNameLength || string.IsNullOrWhiteSpace(shop.ShopName)))
            return null;
        var shopHash = Hash(shops);
        return new(row, shops, shopHash, Hash(new { Term = row, Shops = shopHash, SubscriptionCancellationsV1.Policy }));
    }

    private static SubscriptionCancellationDetailV1 Detail(TermFactsRow row, long version, Guid? change, bool commercial,
        string? sourceHash, string shopHash, int count, CancellationShopV1[] rows)
    {
        var page = rows.Take(SubscriptionCancellationsV1.PageSize).ToArray();
        return new(1, row.AccountId, row.TermId, version, change, row.EndsAt, SubscriptionCancellationsV1.Policy,
            "Asia/Kolkata", "Paid", commercial ? new(row.PricePaise, row.Currency, row.PlanVersionId) : null,
            sourceHash, shopHash, count, page, rows.Length > page.Length ? page[^1].ShopId : null, NoRefund: true, NoGrace: true);
    }

    private static string Hash<T>(T value) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
