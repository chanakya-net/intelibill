using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Changes;

public interface ICapacityIncreaseStore
{
    Task<(CapacityChangeQuoteV1 Quote, long Revision)?> ReadAsync(CheckoutQuoteV1 order, CancellationToken ct);
    Task<bool> InFlightAsync(Guid account, Guid term, CancellationToken ct);
    Task FundAsync(CapacityReservationV1 command, Guid? attempt, Guid? receipt, CancellationToken ct);
    Task<string?> StateAsync(Guid account, Guid order, CancellationToken ct);
}

/// <summary>Account-locked settlement. A null command leaves the verified funds in P03 paid review.</summary>
public sealed class ApplyCapacityIncreaseHandler(ICapacityIncreaseStore store, ICapacityChangeQuoteStore quotes)
{
    public async Task<CapacityReservationV1?> PrepareAsync(CheckoutQuoteV1 order, DateTimeOffset earliest,
        DateTimeOffset latest, DateTimeOffset now, CancellationToken ct)
    {
        if (order.TermId is not { } term || order.Acceptance is not { } accepted || earliest < accepted.AcceptedAt ||
            latest < earliest || latest > now || now >= order.PreviewEndsAt ||
            await store.InFlightAsync(order.AccountId, term, ct) ||
            await store.ReadAsync(order, ct) is not { } sealedChange)
            return null;
        var (q, revision) = sealedChange;
        var source = await quotes.SourceAsync(order.AccountId, term, now, ct);
        if (order.Increase?.SourceRevision != revision || source is not { CompleteEvidence: true, Superseded: false, Closed: false } ||
            source.Hash() != q.SourceHash || source.Suspended != q.SuspensionDisclosed || q.SnapshotHash != order.Increase?.QuoteHash)
            return null;
        var bindings = string.Join(',', source.Shops.OrderBy(s => s.ShopId).Select(s => $"{s.ShopId:D}/{s.RequestId:D}"));
        var digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(bindings)));
        return new(1, Guid.NewGuid(), CapacityReservationV1.Reserve, order.OrderId, order.AccountId, term,
            source.Shops[0].ShopId, digest, source.Shops.Length, q.SourceHash, q.PriorCapacity, q.PriorMemberAllowance,
            q.TargetCapacity, q.TargetMemberAllowance, q.IntervalStartsAt, q.PaidThrough,
            now.AddMinutes(5) < q.PaidThrough ? now.AddMinutes(5) : q.PaidThrough, revision, revision + 1, null);
    }

    public Task FundAsync(CapacityReservationV1 command, Guid? attempt, Guid? receipt, CancellationToken ct) =>
        store.FundAsync(command, attempt, receipt, ct);
}
