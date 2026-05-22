using System.Globalization;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

public sealed class SyncOfflineSalesCommandHandler(
    IUserRepository userRepository,
    IInvoiceLeaseRepository invoiceLeaseRepository,
    ISaleLineValidator saleLineValidator,
    ICustomerResolver customerResolver,
    ISaleRepository saleRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
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
            var normalizedClientSaleId = string.IsNullOrWhiteSpace(sale.ClientSaleId)
                ? string.Empty
                : sale.ClientSaleId.Trim();

            if (string.IsNullOrWhiteSpace(normalizedClientSaleId))
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.ClientSaleIdRequired));
                continue;
            }

            if (normalizedClientSaleId.Length > 120)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.ClientSaleIdTooLong));
                continue;
            }

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

            var invoiceNumber = string.IsNullOrWhiteSpace(sale.InvoiceNumber)
                ? string.Empty
                : sale.InvoiceNumber.Trim();

            if (string.IsNullOrWhiteSpace(invoiceNumber))
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.InvoiceNumberRequired));
                continue;
            }

            if (invoiceNumber.Length > 40)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.InvoiceNumberTooLong));
                continue;
            }

            if (sale.Items.Count == 0)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.ItemsRequired));
                continue;
            }

            if (sale.PaidAmount < 0)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.PaidAmountInvalid));
                continue;
            }

            if (sale.DueAmount < 0)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.DueAmountInvalid));
                continue;
            }

            if (sale.PaymentMethod == PaymentMethod.Credit && sale.DueAmount <= 0)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.CreditRequiresDueAmount));
                continue;
            }

            if (sale.DueAmount > 0 && !sale.CustomerId.HasValue && string.IsNullOrWhiteSpace(sale.CustomerPhone))
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.CustomerIdentityRequiredForDue));
                continue;
            }

            if (sale.TotalAmount < 0)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.TotalAmountInvalid));
                continue;
            }

            if (sale.TotalTaxAmount < 0)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.TotalTaxAmountInvalid));
                continue;
            }

            if (sale.SubtotalBeforeDiscount < 0)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.SubtotalBeforeDiscountInvalid));
                continue;
            }

            if (sale.TotalBeforeDiscount < 0)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.TotalBeforeDiscountInvalid));
                continue;
            }

            if (sale.TotalDiscountAmount < 0)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.TotalDiscountAmountInvalid));
                continue;
            }

            if (!AmountsMatch(sale.PaidAmount, sale.DueAmount, sale.TotalAmount))
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.PaidAndDueAmountMismatch));
                continue;
            }

            var customerResolution = await customerResolver.ResolveAsync(
                command.ShopId,
                sale.CustomerId,
                sale.CustomerPhone,
                hasDueAmount: sale.DueAmount > 0,
                sale.PaymentMethod,
                cancellationToken);

            if (customerResolution.IsError)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, customerResolution.FirstError));
                continue;
            }

            var resolvedCustomer = customerResolution.Value;

            if (!TryMatchLease(activeLeases, invoiceNumber, out _))
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.InvoiceLeaseNotFound));
                continue;
            }

            var existingInvoiceSale = await saleRepository.GetByInvoiceNumberAsync(
                command.ShopId,
                invoiceNumber,
                cancellationToken);

            if (existingInvoiceSale is not null)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.InvoiceNumberAlreadyUsed));
                continue;
            }

            var lineCommands = new List<RecordSaleItemCommand>(sale.Items.Count);
            var lineByKey = new Dictionary<string, OfflineSaleSyncLineCommand>(sale.Items.Count);

            for (var i = 0; i < sale.Items.Count; i++)
            {
                var line = sale.Items[i];

                if (string.IsNullOrWhiteSpace(line.Barcode))
                {
                    results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.BarcodeRequired));
                    goto NextSale;
                }

                if (string.IsNullOrWhiteSpace(line.BatchNumber))
                {
                    results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.BatchNumberRequired));
                    goto NextSale;
                }

                if (string.IsNullOrWhiteSpace(line.ItemName))
                {
                    results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.ItemNameRequired));
                    goto NextSale;
                }

                if (line.InventoryBatchId == Guid.Empty)
                {
                    results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.InventoryBatchIdRequired));
                    goto NextSale;
                }

                if (line.Quantity <= 0)
                {
                    results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.OfflineLineQuantityMustBePositive));
                    goto NextSale;
                }

                if (line.PreTaxAmountBeforeDiscount < 0
                    || line.ItemDiscountAmount < 0
                    || line.SaleDiscountAmount < 0
                    || line.TaxableAmount < 0
                    || line.TaxAmount < 0
                    || line.TotalAmount < 0)
                {
                    results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.OfflineLineAmountsInvalid));
                    goto NextSale;
                }

                var lineKey = i.ToString(CultureInfo.InvariantCulture);
                lineByKey.Add(lineKey, line);
                lineCommands.Add(new RecordSaleItemCommand(
                    line.Barcode,
                    line.BatchNumber,
                    line.ItemName,
                    line.Quantity,
                    line.CostPrice,
                    line.SalesPrice,
                    line.Mrp,
                    line.TaxRatePercent,
                    line.IsPriceIncludingTax,
                    line.InventoryBatchId,
                    ItemDiscount: null,
                    ClientLineKey: lineKey,
                    HsnCode: line.HsnCode));
            }

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
                if (string.IsNullOrWhiteSpace(validated.Command.ClientLineKey)
                    || !lineByKey.TryGetValue(validated.Command.ClientLineKey, out var line))
                {
                    results.Add(BuildErrorResult(
                        normalizedClientSaleId,
                        Errors.General.Unexpected("Offline sale line mapping failed.")));
                    goto NextSale;
                }

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

            var offlineIdempotencyKey = OfflineSaleSyncIdempotencyHasher.ComputeKey(deviceId, normalizedClientSaleId);
            var saleEntity = Sale.Create(
                command.ShopId,
                command.ActorUserId,
                offlineIdempotencyKey,
                requestHash,
                invoiceNumber,
                resolvedCustomer?.Id ?? sale.CustomerId,
                resolvedCustomer?.Name ?? sale.CustomerName,
                resolvedCustomer?.PhoneNumber ?? sale.CustomerPhone,
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
                    invoiceNumber,
                    null,
                    sale.SoldAt,
                    command.ActorUserId,
                    command.ActorUserId);

                if (txResult.IsError)
                    throw new InvalidOperationException(txResult.FirstError.Description);

                await stockTransactionRepository.AddAsync(txResult.Value, cancellationToken);
            }

            await saleRepository.AddAsync(saleEntity, cancellationToken);

            if (saleEntity.DueAmount > 0 && saleEntity.CustomerId.HasValue)
            {
                var ledgerResult = CustomerLedgerEntry.Create(
                    saleEntity.ShopId,
                    saleEntity.CustomerId.Value,
                    saleEntity.Id,
                    CustomerLedgerEntryType.SaleDue,
                    saleEntity.DueAmount,
                    DateOnly.FromDateTime(saleEntity.SoldAt.UtcDateTime),
                    $"Due recorded from sale {saleEntity.InvoiceNumber}",
                    command.ActorUserId);

                if (ledgerResult.IsError)
                {
                    results.Add(BuildErrorResult(normalizedClientSaleId, ledgerResult.FirstError));
                    continue;
                }

                await customerLedgerEntryRepository.AddAsync(ledgerResult.Value, cancellationToken);
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                unitOfWork.ClearChanges();

                var concurrentSale = await saleRepository.GetByClientSaleIdAsync(
                    command.ShopId,
                    deviceId,
                    normalizedClientSaleId,
                    cancellationToken);

                if (concurrentSale is null)
                {
                    results.Add(BuildErrorResult(normalizedClientSaleId, GetSaveFailureError(ex)));
                    continue;
                }

                if (!string.Equals(concurrentSale.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.IdempotencyConflict));
                    continue;
                }

                results.Add(new OfflineSaleSyncResultDto(
                    normalizedClientSaleId,
                    StatusDuplicate,
                    concurrentSale.Id,
                    concurrentSale.InvoiceNumber,
                    []));
                continue;
            }

            results.Add(new OfflineSaleSyncResultDto(
                normalizedClientSaleId,
                StatusCreated,
                saleEntity.Id,
                saleEntity.InvoiceNumber,
                []));

        NextSale:
            continue;
        }

        return new OfflineSalesSyncResponseDto(results);
    }

    private static bool AmountsMatch(decimal paidAmount, decimal dueAmount, decimal totalAmount) =>
        decimal.Round(paidAmount + dueAmount, 2, MidpointRounding.AwayFromZero) ==
        decimal.Round(totalAmount, 2, MidpointRounding.AwayFromZero);

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

    private static Error GetSaveFailureError(DbUpdateException exception) =>
        ContainsExceptionText(exception, "ix_sales_shop_id_invoice_number")
            ? Errors.Sale.InvoiceNumberAlreadyUsed
            : Errors.General.Unexpected("Offline sale could not be synced. Please retry.");

    private static bool ContainsExceptionText(Exception exception, string text)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(text, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
