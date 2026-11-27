using System.Runtime.CompilerServices;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.OfflineSalesSnapshot.DTOs;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;
using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Common.Time;

namespace Intelibill.Application.Features.OfflineSalesSnapshot.Services;

public sealed class OfflineSalesSnapshotStreamingService(
    IUserRepository userRepository,
    ICustomerRepository customerRepository,
    IDiscountRuleRepository discountRuleRepository,
    IInvoiceLeaseRepository invoiceLeaseRepository,
    IServiceRepository serviceRepository,
    IOfflineExpiryEvidenceRepository expiryEvidence,
    IExpiryPolicyRepository expiryPolicies,
    IExpiryPolicyFence expiryFence,
    IUnitOfWork unitOfWork,
    IShopRepository shops,
    IBusinessClock clock) : IOfflineSalesSnapshotStreamingService
{
    public async Task<ErrorOr<Success>> ValidateAccessAsync(
        Guid userId,
        Guid activeShopId,
        CancellationToken cancellationToken)
    {
        var caller = await userRepository.GetByIdWithDetailsAsync(userId, cancellationToken);
        if (caller is null)
            return Errors.Auth.UserNotFound;

        var isMember = caller.ShopMemberships.Any(sm => sm.ShopId == activeShopId);
        if (!isMember)
            return Errors.Shop.MembershipNotFound;

        return Result.Success;
    }

    public async IAsyncEnumerable<IOfflineSalesSnapshotStreamRecord> StreamAsync(
        Guid activeShopId,
        Guid snapshotId,
        DateTimeOffset startedAt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var shop = await shops.GetByIdAsync(activeShopId, cancellationToken)
            ?? throw new InvalidOperationException("Snapshot shop is unavailable.");
        var first = await CapturePageAsync(activeShopId, snapshotId, null, null, cancellationToken);
        var catalogVersion = first.Version;
        yield return new OfflineSalesSnapshotMetadataRecord(
            new OfflineSalesSnapshotMetadata(snapshotId, activeShopId, SchemaVersion: 4, StartedAt: startedAt,
                BusinessTimeZoneId: shop.BusinessTimeZoneId, CatalogVersion: catalogVersion));

        var batchCount = 0;
        var page = first;
        while (true)
        {
            foreach (var batch in page.Batches)
            {
                yield return new OfflineSalesSnapshotBatchRecord(batch);
                batchCount++;
            }
            if (page.Batches.Count < CapturePageSize) break;
            page = await CapturePageAsync(activeShopId, snapshotId, page.Batches[^1].BatchId, catalogVersion, cancellationToken);
        }

        var customerCount = 0;
        await foreach (var customer in customerRepository.StreamActiveByShopAsync(activeShopId, cancellationToken))
        {
            yield return new OfflineSalesSnapshotCustomerRecord(
                new OfflineCustomerLiteDto(customer.Id, customer.Name, customer.PhoneNumber, customer.Gstin, customer.StateCode, customer.Address));
            customerCount++;
        }

        var discountRuleCount = 0;
        await foreach (var rule in discountRuleRepository.StreamActiveByShopAsync(activeShopId, startedAt, cancellationToken))
        {
            yield return new OfflineSalesSnapshotDiscountRuleRecord(
                new OfflineDiscountRuleDto(
                    RuleId: rule.Id,
                    RuleType: rule.RuleType,
                    Name: rule.Name,
                    Description: rule.Description,
                    InventoryBatchId: rule.InventoryBatchId,
                    Percentage: rule.Percentage,
                    ThresholdAmount: rule.ThresholdAmount,
                    StartsAt: rule.StartsAt,
                    EndsAt: rule.EndsAt,
                    BelowCostConfirmed: rule.BelowCostConfirmed));
            discountRuleCount++;
        }

        var activeLeaseCount = 0;
        await foreach (var lease in invoiceLeaseRepository.StreamActiveByShopAsync(activeShopId, startedAt, cancellationToken))
        {
            yield return new OfflineSalesSnapshotActiveLeaseRecord(
                new OfflineActiveLeaseDto(
                    LeaseId: lease.Id,
                    InvoiceSequenceId: lease.InvoiceSequenceId,
                    DeviceId: lease.DeviceId,
                    FiscalYearStart: lease.FiscalYearStart,
                    Prefix: lease.Prefix,
                    RangeStart: lease.RangeStart,
                    RangeEnd: lease.RangeEnd,
                    NextNumber: lease.NextNumber,
                    NumberPadding: lease.NumberPadding,
                    ReservedAt: lease.ReservedAt,
                    ExpiresAt: lease.ExpiresAt,
                    SellerProfile: lease.SellerProfile));
            activeLeaseCount++;
        }

        var serviceCount = 0;
        await foreach (var service in serviceRepository.StreamActiveByShopAsync(activeShopId, cancellationToken))
        {
            yield return new OfflineSalesSnapshotServiceRecord(
                new OfflineSellableServiceDto(
                    ServiceId: service.Id,
                    Code: service.Code,
                    Name: service.Name,
                    Price: service.Price,
                    TaxRatePercent: service.TaxRatePercent,
                    TaxIncluded: service.TaxIncluded,
                    HsnCode: service.HsnCode));
            serviceCount++;
        }

        if ((await expiryPolicies.GetStateAsync(activeShopId, cancellationToken))?.CatalogVersion != catalogVersion)
            throw new InvalidOperationException("Expiry catalog changed during capture; refresh the complete snapshot.");
        yield return new OfflineSalesSnapshotCompleteRecord(
            new OfflineSalesSnapshotComplete(
                SnapshotId: snapshotId,
                CompletedAt: DateTimeOffset.UtcNow,
                BatchCount: batchCount,
                CustomerCount: customerCount,
                DiscountRuleCount: discountRuleCount,
                ActiveLeaseCount: activeLeaseCount,
                ServiceCount: serviceCount));
    }
    private const int CapturePageSize = 100;
    private async Task<(long Version, IReadOnlyList<OfflineSellableBatchDto> Batches)> CapturePageAsync(
        Guid shopId, Guid snapshotId, Guid? after, long? expectedVersion, CancellationToken ct)
    {
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var fence = await expiryFence.AcquireAsync(shopId, false, ct);
            if (fence.IsError) throw new InvalidOperationException("Expiry catalog is busy or unavailable.");
            var state = await expiryPolicies.GetStateAsync(shopId, ct);
            if (state is null || expectedVersion.HasValue && state.CatalogVersion != expectedVersion.Value ||
                !await expiryEvidence.HasRetainedCatalogAsync(shopId, state.CatalogVersion, ct))
                throw new InvalidOperationException("Expiry catalog history is unavailable or changed during capture.");
            // Fully consume the bounded SELECT before adding/saving evidence on this connection.
            var batches = await expiryEvidence.GetCapturePageAsync(shopId, after, CapturePageSize, ct);
            var policies = await expiryPolicies.GetEffectiveForItemsAsync(shopId, batches.Select(x => x.ItemId).Distinct().ToArray(), ct);
            var at = clock.UtcNow;
            var date = clock.GetBusinessDate(at, (await shops.GetByIdAsync(shopId, ct))!.BusinessTimeZoneId);
            var result = new List<OfflineSellableBatchDto>(batches.Count);
            foreach (var batch in batches)
            {
                if (!policies.TryGetValue(batch.ItemId, out var effective) || !effective.HistoryKnown || effective.Policy is null)
                    throw new InvalidOperationException("Expiry catalog policy is unavailable.");
                var proof = OfflineExpiryEvidence.Capture(snapshotId, batch, effective.Policy, state.CatalogVersion, at);
                await expiryEvidence.AddAsync(proof, ct);
                var decision = ExpiryPolicyEvaluator.Evaluate(proof.BatchFacts, proof.EffectivePolicy, proof.CatalogVersion, date);
                result.Add(new(batch.Id, batch.ItemId, batch.Item.Name, batch.Item.Barcode, batch.Item.Uom, batch.Item.HsnCode,
                    batch.BatchNumber, batch.Quantity, batch.CostPrice, batch.Mrp, batch.SalesPrice, batch.TaxRatePercent,
                    batch.TaxIncluded, batch.PurchaseTaxIncluded, batch.ExpiryDate, batch.Item.QuantityScale, batch.Item.DocumentUnit,
                    proof.Id, proof.EffectivePolicy, batch.Quantity, batch.QuarantinedQuantity, decision.AvailableQuantity,
                    proof.CapturedAt, proof.ExpiresAt, batch.Version, batch.ManufacturingDate));
            }
            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitTransactionAsync(ct);
            return (state.CatalogVersion, result);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }
    }

}

