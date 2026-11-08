using ErrorOr;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
namespace Intelibill.Application.Features.Expiry;

public interface IOfflineExpiryAuthority
{
    Task<ErrorOr<IReadOnlyList<SaleExpiryDecision>>> ValidateAsync(OfflineSalePostingRequest request, IReadOnlyList<ValidatedSaleLine> lines, CancellationToken ct);
    Task<ErrorOr<IReadOnlyList<SaleExpiryDecision>>> ValidateFrozenAsync(Guid shopId, OfflineSaleSyncCommand sale, DateTimeOffset now, CancellationToken ct);
    Task<bool> VerifiesPostedSaleAsync(OfflineSubmission submission, Sale sale, CancellationToken ct);
}

/// <summary>Historical authority only. Never replaces it with today's online guard or a manager reason.</summary>
public sealed class OfflineExpiryAuthority(IOfflineExpiryEvidenceRepository evidence, IExpiryPolicyRepository policies, IShopRepository shops, IBusinessClock clock) : IOfflineExpiryAuthority
{
    public static Error ReviewRequired => Error.Conflict("Expiry.ReviewRequired", "The issued invoice needs trusted expiry evidence review.");
    public async Task<ErrorOr<IReadOnlyList<SaleExpiryDecision>>> ValidateAsync(OfflineSalePostingRequest request, IReadOnlyList<ValidatedSaleLine> lines, CancellationToken ct)
    {
        if (lines.Where(x => x.Batch is not null).Any(x => x.Batch!.ShopId != request.ShopId || !request.Sale.Items.Any(i => !i.IsServiceLine && i.InventoryBatchId == x.Batch.Id))) return ReviewRequired;
        return await ValidateFrozenAsync(request.ShopId, request.Sale, request.Now, ct);
    }
    public async Task<ErrorOr<IReadOnlyList<SaleExpiryDecision>>> ValidateFrozenAsync(Guid shopId, OfflineSaleSyncCommand sale, DateTimeOffset now, CancellationToken ct)
    {
        if (sale.Items is null || sale.Items.Any(x => x is null)) return ReviewRequired;
        if (sale.Items.Any(x => x.ExpiryEvidence is not null) && sale.Items.Any(x => !x.IsServiceLine && x.ExpiryEvidence is null)) return ReviewRequired;
        var shop = await shops.GetByIdAsync(shopId, ct);
        if (shop is null || sale.SoldAt > now) return ReviewRequired;
        var date = clock.GetBusinessDate(sale.SoldAt, shop.BusinessTimeZoneId);
        var decisions = new List<SaleExpiryDecision>();
        foreach (var line in sale.Items)
        {
            if (line.IsServiceLine) { if (line.ExpiryEvidence is not null) return ReviewRequired; continue; }
            var historical = await policies.GetEffectiveAsync(shopId, line.ExpiryEvidence is { } supplied
                ? (await evidence.GetAsync(shopId, supplied.EvidenceId, ct))?.ItemId ?? Guid.Empty
                : await ResolveLegacyItemAsync(shopId, line.InventoryBatchId, ct), sale.SoldAt, ct);
            if (!historical.HistoryKnown || historical.Policy is null) return ReviewRequired;
            if (line.ExpiryEvidence is not { } proof)
            {
                if (!historical.IsPreFeatureBaseline || historical.Policy != ExpiryPolicy.Default ||
                    !await evidence.HasRetainedCatalogAsync(shopId, historical.CatalogVersion, ct)) return ReviewRequired;
                // Pre-feature Ignore proves policy eligibility, not historical date or batch-xmin facts.
                decisions.Add(new(Guid.Empty, await ResolveLegacyItemAsync(shopId, line.InventoryBatchId, ct), line.InventoryBatchId,
                    line.Quantity, null, null, historical.Policy, historical.CatalogVersion, 0, date, null, false, null, null, null));
                continue;
            }
            var retained = await evidence.GetAsync(shopId, proof.EvidenceId, ct);
            if (retained is null || retained.BatchId != line.InventoryBatchId || await evidence.GetBatchItemAsync(shopId, retained.BatchId, ct) != retained.ItemId || retained.BatchFacts.BatchId != retained.BatchId || retained.BatchFacts.ItemId != retained.ItemId ||
                retained.CapturedAt > sale.SoldAt || sale.SoldAt > retained.ExpiresAt || retained.CatalogVersion != proof.CatalogVersion ||
                retained.EffectivePolicy.Version != proof.PolicyVersion || retained.BatchFacts.ExpiryDate != proof.ExpiryDate || proof.BusinessDate != date ||
                historical.Policy != retained.EffectivePolicy || !await evidence.HasRetainedCatalogAsync(shopId, retained.CatalogVersion, ct) ||
                await evidence.HasRelevantChangeAsync(shopId, retained.ItemId, retained.BatchId, retained.CatalogVersion, sale.SoldAt, ct)) return ReviewRequired;
            var decision = ExpiryPolicyEvaluator.Evaluate(retained.BatchFacts, retained.EffectivePolicy, retained.CatalogVersion, date);
            if (!decision.Allowed || decision.RequiresApproval || (decision.ReasonCode ?? "Allowed") != proof.DecisionCode) return ReviewRequired;
            decisions.Add(new(Guid.Empty, retained.ItemId, retained.BatchId, line.Quantity, retained.BatchFacts.ExpiryDate, retained.BatchFacts.ManufacturingDate,
                retained.EffectivePolicy, retained.CatalogVersion, retained.BatchFacts.BatchVersion, date, decision.ReasonCode, decision.Warning, null, null, null));
        }
        return decisions;
    }
    private async Task<Guid> ResolveLegacyItemAsync(Guid shopId, Guid batchId, CancellationToken ct)
    {
        // Legacy envelopes retain batch identity but predate per-lot evidence.
        return await evidence.GetBatchItemAsync(shopId, batchId, ct) ?? Guid.Empty;
    }
    public async Task<bool> VerifiesPostedSaleAsync(OfflineSubmission submission, Sale sale, CancellationToken ct)
    {
        if (sale.ShopId != submission.ShopId || sale.RequestHash != submission.PayloadHash || sale.DeviceId != submission.DeviceId || sale.ClientSaleId != submission.ClientSaleId) return false;
        OfflineSaleSyncCommand? payload;
        try { payload = OfflineSubmissionPayload.Deserialize(submission.PayloadJson); } catch (System.Text.Json.JsonException) { return false; }
        if (payload?.Items is null || payload.Items.Any(x => x is null) || payload.Items.Where(x => !x.IsServiceLine).Any(x => x.ExpiryEvidence is null) ||
            payload.ClientSaleId?.Trim() != submission.ClientSaleId || payload.InvoiceNumber?.Trim() != submission.InvoiceNumber ||
            payload.SoldAt.UtcTicks / 10 != submission.ClaimedSoldAt.UtcTicks / 10 || sale.SoldAt.UtcTicks / 10 != submission.ClaimedSoldAt.UtcTicks / 10 ||
            sale.InvoiceNumber != submission.InvoiceNumber ||
            OfflineSaleSyncIdempotencyHasher.ComputeHash(submission.ShopId, submission.DeviceId, payload) != submission.PayloadHash) return false;
        var validated = await ValidateFrozenAsync(submission.ShopId, payload, submission.ReceivedAt > sale.SoldAt ? submission.ReceivedAt : sale.SoldAt, ct);
        return !validated.IsError && sale.ExpiryDecisions is not null && validated.Value.Count == sale.ExpiryDecisions.Count &&
            validated.Value.Zip(sale.ExpiryDecisions).All(pair => pair.First with { SaleId = sale.Id } == pair.Second);
    }
}
