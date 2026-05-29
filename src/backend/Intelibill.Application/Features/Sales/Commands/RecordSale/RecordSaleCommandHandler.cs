using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Application.Features.Sales.Services.Pricing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

public sealed class RecordSaleCommandHandler
{
    private readonly ISaleLineValidator saleLineValidator;
    private readonly ISalePricingCalculator salePricingCalculator;
    private readonly ICustomerResolver customerResolver;
    private readonly ISaleRepository saleRepository;
    private readonly SaleDtoBuilder saleDtoBuilder;
    private readonly ICustomerLedgerEntryRepository customerLedgerEntryRepository;
    private readonly IStockTransactionRepository stockTransactionRepository;
    private readonly IUnitOfWork unitOfWork;

    public RecordSaleCommandHandler(
        ISaleLineValidator saleLineValidator,
        ISalePricingCalculator salePricingCalculator,
        ICustomerResolver customerResolver,
        ISaleRepository saleRepository,
        SaleDtoBuilder saleDtoBuilder,
        ICustomerLedgerEntryRepository customerLedgerEntryRepository,
        IStockTransactionRepository stockTransactionRepository,
        IUnitOfWork unitOfWork)
    {
        this.saleLineValidator = saleLineValidator;
        this.salePricingCalculator = salePricingCalculator;
        this.customerResolver = customerResolver;
        this.saleRepository = saleRepository;
        this.saleDtoBuilder = saleDtoBuilder;
        this.customerLedgerEntryRepository = customerLedgerEntryRepository;
        this.stockTransactionRepository = stockTransactionRepository;
        this.unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<SaleDto>> HandleAsync(RecordSaleCommand command, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var normalizedIdempotencyKey = command.IdempotencyKey.Trim();
        var requestHash = RecordSaleIdempotencyHasher.ComputeHash(command);

        var existingSale = await saleRepository.GetByIdempotencyKeyAsync(command.ShopId, command.ActorUserId, normalizedIdempotencyKey, cancellationToken);
        if (existingSale is not null)
        {
            if (!string.Equals(existingSale.RequestHash, requestHash, StringComparison.Ordinal))
                return Errors.Sale.IdempotencyConflict;

            return await saleDtoBuilder.BuildSaleDtoAsync(existingSale, existingSale.Warnings, cancellationToken);
        }

        var validationResultOrError = await saleLineValidator.ValidateLinesAsync(command.ShopId, command.Items, warnings, cancellationToken);
        if (validationResultOrError.IsError)
            return validationResultOrError.Errors;

        var (validatedLines, itemNameById) = validationResultOrError.Value;
        var soldAt = DateTimeOffset.UtcNow;
        var effectiveSaleDiscount = command.SaleDiscount ?? new InstantDiscount(InstantDiscountType.None, 0m);
        var pricingRequest = new SalePricingCalculationRequest(
            command.ShopId,
            soldAt,
            validatedLines.Select(line => new SalePricingLineCalculationRequest(
                line.LineType,
                line.Batch?.Id ?? Guid.Empty,
                line.Service?.Id,
                line.Command.Quantity,
                line.LineType == SaleLineType.Goods ? line.Batch!.GetProfitCostPrice() : 0m,
                line.LineType == SaleLineType.Goods ? line.Batch!.SalesPrice : line.Command.SalesPrice,
                line.LineType == SaleLineType.Goods ? line.Batch!.Mrp : line.Command.Mrp,
                line.Command.TaxRatePercent,
                line.Command.IsPriceIncludingTax,
                line.Command.ItemDiscount ?? new InstantDiscount(InstantDiscountType.None, 0m),
                line.LineType == SaleLineType.Goods)).ToList(),
            effectiveSaleDiscount);

        var pricingOrError = await salePricingCalculator.CalculateAsync(pricingRequest, cancellationToken);
        if (pricingOrError.IsError)
            return pricingOrError.Errors;
        var pricing = pricingOrError.Value;

        var normalizedCustomerPhone = string.IsNullOrWhiteSpace(command.CustomerPhone) ? null : command.CustomerPhone.Trim();
        var resolvedCustomerOrError = await customerResolver.ResolveAsync(command.ShopId, command.CustomerId, command.CustomerPhone, command.DueAmount > 0, command.PaymentMethod, cancellationToken);
        if (resolvedCustomerOrError.IsError)
            return resolvedCustomerOrError.Errors;

        var resolvedCustomer = resolvedCustomerOrError.Value;
        var invoiceNumber = $"INV-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        var saleLineInputs = validatedLines.Select((line, index) =>
        {
            var calculation = pricing.Lines[index];
            return new SaleLineInput(
                command.ShopId,
                line.LineType,
                line.Item?.Id,
                line.Batch?.Id,
                line.Service?.Id,
                LineName: line.Item?.Name ?? line.Service!.Name,
                LineCode: line.Item?.Barcode ?? line.Service!.Code,
                line.Command.Quantity,
                calculation.CostPrice,
                calculation.SalesPrice,
                line.LineType == SaleLineType.Goods ? line.Batch!.Mrp : line.Command.Mrp,
                calculation.TaxRatePercent,
                calculation.IsPriceIncludingTax,
                line.HasPriceMismatch,
                calculation.PreTaxAmountBeforeDiscount,
                calculation.ItemDiscountAmount,
                calculation.SaleDiscountAmount,
                calculation.TaxableAmount,
                calculation.TaxAmount,
                calculation.TotalAmount,
                calculation.ConfiguredBatchRuleId,
                calculation.ConfiguredBatchRulePercentage,
                (line.Command.ItemDiscount ?? new InstantDiscount(InstantDiscountType.None, 0m)).Type,
                (line.Command.ItemDiscount ?? new InstantDiscount(InstantDiscountType.None, 0m)).Value,
                line.Command.HsnCode);
        }).ToList();

        var saleOrError = Sale.Record(
            command.ShopId,
            command.ActorUserId,
            normalizedIdempotencyKey,
            requestHash,
            invoiceNumber,
            saleLineInputs,
            resolvedCustomer?.Id ?? command.CustomerId,
            resolvedCustomer?.Name ?? command.CustomerName,
            resolvedCustomer?.PhoneNumber ?? normalizedCustomerPhone,
            command.PaymentMethod,
            command.PaidAmount,
            command.DueAmount,
            soldAt,
            pricing.ConfiguredSaleRule?.RuleId,
            pricing.ConfiguredSaleRule?.RuleType,
            pricing.ConfiguredSaleRule?.Percentage,
            pricing.ConfiguredSaleRule?.ThresholdAmount,
            effectiveSaleDiscount.Type,
            effectiveSaleDiscount.Value,
            warnings);
        if (saleOrError.IsError)
            return saleOrError.Errors;

        var sale = saleOrError.Value;
        foreach (var line in validatedLines.Where(x => x.LineType == SaleLineType.Goods))
        {
            var cmdItem = line.Command;
            var item = line.Item!;
            var batch = line.Batch!;
            var inventory = line.Inventory!;
            var batchResult = batch.SubtractQuantity(cmdItem.Quantity, command.ActorUserId);
            if (batchResult.IsError) return batchResult.Errors;

            var inventoryResult = inventory.SubtractQuantity(cmdItem.Quantity, command.ActorUserId);
            if (inventoryResult.IsError) return inventoryResult.Errors;

            var txResult = StockTransaction.Create(command.ShopId, item.Id, batch.Id, StockTransactionType.Out, -cmdItem.Quantity, invoiceNumber, null, soldAt, command.ActorUserId, command.ActorUserId);
            if (txResult.IsError) return txResult.Errors;
            await stockTransactionRepository.AddAsync(txResult.Value, cancellationToken);
        }

        await saleRepository.AddAsync(sale, cancellationToken);
        if (sale.DueAmount > 0 && sale.CustomerId.HasValue)
        {
            var ledgerResult = CustomerLedgerEntry.Create(
                sale.ShopId,
                sale.CustomerId.Value,
                sale.Id,
                CustomerLedgerEntryType.SaleDue,
                sale.DueAmount,
                DateOnly.FromDateTime(sale.SoldAt.UtcDateTime),
                $"Due recorded from sale {sale.InvoiceNumber}",
                command.ActorUserId);
            if (ledgerResult.IsError)
                return ledgerResult.Errors;

            await customerLedgerEntryRepository.AddAsync(ledgerResult.Value, cancellationToken);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var concurrentSale = await saleRepository.GetByIdempotencyKeyAsync(command.ShopId, command.ActorUserId, normalizedIdempotencyKey, cancellationToken);
            if (concurrentSale is null)
                throw;

            if (!string.Equals(concurrentSale.RequestHash, requestHash, StringComparison.Ordinal))
                return Errors.Sale.IdempotencyConflict;

            return await saleDtoBuilder.BuildSaleDtoAsync(concurrentSale, concurrentSale.Warnings, cancellationToken);
        }

        return saleDtoBuilder.BuildSaleDto(sale, itemNameById, sale.Warnings);
    }
}
