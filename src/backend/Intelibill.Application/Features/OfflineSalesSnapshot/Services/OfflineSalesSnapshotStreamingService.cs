using System.Runtime.CompilerServices;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.OfflineSalesSnapshot.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.OfflineSalesSnapshot.Services;

public sealed class OfflineSalesSnapshotStreamingService(
    IUserRepository userRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    ICustomerRepository customerRepository,
    IDiscountRuleRepository discountRuleRepository,
    IInvoiceLeaseRepository invoiceLeaseRepository) : IOfflineSalesSnapshotStreamingService
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
        yield return new OfflineSalesSnapshotMetadataRecord(
            new OfflineSalesSnapshotMetadata(snapshotId, activeShopId, SchemaVersion: 1, StartedAt: startedAt));

        var batchCount = 0;
        await foreach (var batch in inventoryBatchRepository.StreamActiveSellableWithItemByShopAsync(activeShopId, cancellationToken))
        {
            yield return new OfflineSalesSnapshotBatchRecord(
                new OfflineSellableBatchDto(
                    BatchId: batch.Id,
                    ItemId: batch.ItemId,
                    ItemName: batch.Item.Name,
                    Barcode: batch.Item.Barcode,
                    Uom: batch.Item.Uom,
                    HsnCode: batch.Item.HsnCode,
                    BatchNumber: batch.BatchNumber,
                    Quantity: batch.Quantity,
                    CostPrice: batch.CostPrice,
                    Mrp: batch.Mrp,
                    SalesPrice: batch.SalesPrice,
                    TaxRatePercent: batch.TaxRatePercent,
                    TaxIncluded: batch.TaxIncluded,
                    PurchaseTaxIncluded: batch.PurchaseTaxIncluded,
                    ExpiryDate: batch.ExpiryDate));
            batchCount++;
        }

        var customerCount = 0;
        await foreach (var customer in customerRepository.StreamActiveByShopAsync(activeShopId, cancellationToken))
        {
            yield return new OfflineSalesSnapshotCustomerRecord(
                new OfflineCustomerLiteDto(customer.Id, customer.Name, customer.PhoneNumber));
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
                    ExpiresAt: lease.ExpiresAt));
            activeLeaseCount++;
        }

        yield return new OfflineSalesSnapshotCompleteRecord(
            new OfflineSalesSnapshotComplete(
                SnapshotId: snapshotId,
                CompletedAt: DateTimeOffset.UtcNow,
                BatchCount: batchCount,
                CustomerCount: customerCount,
                DiscountRuleCount: discountRuleCount,
                ActiveLeaseCount: activeLeaseCount));
    }
}

