using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

public sealed class SyncOfflineSalesCommandHandler(
    IUserRepository userRepository,
    IInvoiceLeaseRepository invoiceLeaseRepository,
    ISaleLineValidator saleLineValidator,
    ISaleRepository saleRepository,
    IStockTransactionRepository stockTransactionRepository,
    IUnitOfWork unitOfWork)
{
    private const string StatusCreated = "created";
    private const string StatusDuplicate = "duplicate";
    private const string StatusFailed = "failed";

    public async Task<ErrorOr<OfflineSalesSyncResponseDto>> HandleAsync(
        SyncOfflineSalesCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var isMember = actor.ShopMemberships.Any(sm => sm.ShopId == command.ShopId);
        if (!isMember)
            return Errors.Shop.MembershipNotFound;

        var deviceId = command.DeviceId.Trim();
        var now = DateTimeOffset.UtcNow;
        var activeLeases = await invoiceLeaseRepository.GetActiveByDeviceAsync(
            command.ShopId,
            deviceId,
            now,
            cancellationToken);

        var results = new List<OfflineSaleSyncResultDto>(command.Sales.Count);

        foreach (var sale in command.Sales)
        {
            var normalizedClientSaleId = sale.ClientSaleId.Trim();
            var requestHash = OfflineSaleSyncIdempotencyHasher.ComputeHash(command.ShopId, deviceId, sale);

            var existingSale = await saleRepository.GetByClientSaleIdAsync(
                command.ShopId,
                deviceId,
                normalizedClientSaleId,
                cancellationToken);

            if (existingSale is not null)
            {
                if (!string.Equals(existingSale.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.IdempotencyConflict));
                    continue;
                }

                results.Add(new OfflineSaleSyncResultDto(
                    normalizedClientSaleId,
                    StatusDuplicate,
                    existingSale.Id,
                    existingSale.InvoiceNumber,
                    []));
                continue;
            }

            if (!TryMatchLease(activeLeases, sale.InvoiceNumber, out _))
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.InvoiceLeaseNotFound));
                continue;
            }

            var existingInvoiceSale = await saleRepository.GetByInvoiceNumberAsync(
                command.ShopId,
                sale.InvoiceNumber,
                cancellationToken);

            if (existingInvoiceSale is not null)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.InvoiceNumberAlreadyUsed));
                continue;
            }

            var lineCommands = sale.Items.Select(item =>
                new RecordSaleItemCommand(
                    item.Barcode,
                    item.BatchNumber,
                    item.ItemName,
                    item.Quantity,
                    item.CostPrice,
                    item.SalesPrice,
                    item.Mrp,
                    item.TaxRatePercent,
                    item.IsPriceIncludingTax,
                    item.InventoryBatchId,
                    ItemDiscount: null,
                    ClientLineKey: null,
                    HsnCode: item.HsnCode)).ToList();

            var lineMap = lineCommands
                .Zip(sale.Items, (cmd, line) => new { cmd, line })
                .ToDictionary(x => x.cmd, x => x.line);

            var warnings = new List<string>();
            var validation = await saleLineValidator.ValidateLinesAsync(
                command.ShopId,
                lineCommands,
                warnings,
                cancellationToken);

            if (validation.IsError)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, validation.FirstError));
                continue;
            }

            var validatedLines = validation.Value.Lines;
            var saleItems = new List<SaleItem>(validatedLines.Count);

            foreach (var validated in validatedLines)
            {
                var line = lineMap[validated.Command];
                var saleItem = SaleItem.Create(
                    command.ShopId,
                    validated.Item.Id,
                    validated.Batch.Id,
                    line.Quantity,
                    line.CostPrice,
                    line.SalesPrice,
                    line.Mrp,
                    line.TaxRatePercent,
                    line.IsPriceIncludingTax,
                    validated.HasPriceMismatch,
                    preTaxAmountBeforeDiscount: line.PreTaxAmountBeforeDiscount,
                    itemDiscountAmount: line.ItemDiscountAmount,
                    saleDiscountAmount: line.SaleDiscountAmount,
                    taxableAmount: line.TaxableAmount,
                    taxAmount: line.TaxAmount,
                    totalAmount: line.TotalAmount,
                    configuredBatchRuleId: line.ConfiguredBatchRuleId,
                    configuredBatchRulePercentage: line.ConfiguredBatchRulePercentage,
                    itemDiscountOverrideType: line.ItemDiscountOverrideType,
                    itemDiscountOverrideValue: line.ItemDiscountOverrideValue,
                    hsnCode: line.HsnCode);
                saleItems.Add(saleItem);
            }

            if (TryGetInsufficientInventoryLine(validatedLines, out var insufficient))
            {
                results.Add(BuildErrorResult(
                    normalizedClientSaleId,
                    Errors.Sale.InsufficientStock(insufficient.Command.Barcode, insufficient.Command.BatchNumber)));
                continue;
            }

            var offlineIdempotencyKey = OfflineSaleSyncIdempotencyHasher.ComputeKey(deviceId, normalizedClientSaleId);
            var saleEntity = Sale.Create(
                command.ShopId,
                command.ActorUserId,
                offlineIdempotencyKey,
                requestHash,
                sale.InvoiceNumber,
                sale.CustomerId,
                sale.CustomerName,
                sale.CustomerPhone,
                sale.PaymentMethod,
                sale.SoldAt,
                sale.PaidAmount,
                sale.DueAmount,
                sale.TotalAmount,
                sale.TotalTaxAmount,
                saleItems,
                subtotalBeforeDiscount: sale.SubtotalBeforeDiscount,
                totalBeforeDiscount: sale.TotalBeforeDiscount,
                totalDiscountAmount: sale.TotalDiscountAmount,
                configuredSaleRuleId: sale.ConfiguredSaleRuleId,
                configuredSaleRuleType: sale.ConfiguredSaleRuleType,
                configuredSaleRulePercentage: sale.ConfiguredSaleRulePercentage,
                configuredSaleRuleThresholdAmount: sale.ConfiguredSaleRuleThresholdAmount,
                saleDiscountOverrideType: sale.SaleDiscountOverrideType,
                saleDiscountOverrideValue: sale.SaleDiscountOverrideValue,
                source: SaleSource.Offline,
                clientSaleId: normalizedClientSaleId,
                deviceId: deviceId,
                syncedAt: now);

            foreach (var validated in validatedLines)
            {
                var batchResult = validated.Batch.SubtractQuantity(validated.Command.Quantity, command.ActorUserId);
                if (batchResult.IsError)
                    throw new InvalidOperationException(batchResult.FirstError.Description);

                var inventoryResult = validated.Inventory.SubtractQuantity(validated.Command.Quantity, command.ActorUserId);
                if (inventoryResult.IsError)
                    throw new InvalidOperationException(inventoryResult.FirstError.Description);

                var txResult = StockTransaction.Create(
                    command.ShopId,
                    validated.Item.Id,
                    validated.Batch.Id,
                    StockTransactionType.Out,
                    -validated.Command.Quantity,
                    sale.InvoiceNumber,
                    null,
                    sale.SoldAt,
                    command.ActorUserId,
                    command.ActorUserId);

                if (txResult.IsError)
                    throw new InvalidOperationException(txResult.FirstError.Description);

                await stockTransactionRepository.AddAsync(txResult.Value, cancellationToken);
            }

            await saleRepository.AddAsync(saleEntity, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            results.Add(new OfflineSaleSyncResultDto(
                normalizedClientSaleId,
                StatusCreated,
                saleEntity.Id,
                saleEntity.InvoiceNumber,
                []));
        }

        return new OfflineSalesSyncResponseDto(results);
    }

    private static bool TryMatchLease(
        IReadOnlyList<InvoiceLease> leases,
        string invoiceNumber,
        out InvoiceLease? matched)
    {
        foreach (var lease in leases)
        {
            if (!invoiceNumber.StartsWith(lease.Prefix, StringComparison.Ordinal))
                continue;

            var numberPart = invoiceNumber[lease.Prefix.Length..];
            if (!int.TryParse(numberPart, out var number))
                continue;

            if (number < lease.RangeStart || number > lease.RangeEnd)
                continue;

            matched = lease;
            return true;
        }

        matched = null;
        return false;
    }

    private static OfflineSaleSyncResultDto BuildErrorResult(string clientSaleId, Error error) =>
        new(clientSaleId, StatusFailed, null, null, [new OfflineSaleSyncErrorDto(error.Code, error.Description)]);

    private static bool TryGetInsufficientInventoryLine(
        IReadOnlyList<ValidatedSaleLine> lines,
        out ValidatedSaleLine insufficient)
    {
        foreach (var line in lines)
        {
            if (line.Inventory.Quantity - line.Command.Quantity < 0)
            {
                insufficient = line;
                return true;
            }
        }

        insufficient = null!;
        return false;
    }
}
