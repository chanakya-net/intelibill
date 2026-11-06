using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Customers.Services;
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
    private readonly ICreditNoteRepository creditNoteRepository;
    private readonly SaleDtoBuilder saleDtoBuilder;
    private readonly ICustomerLedgerEntryRepository customerLedgerEntryRepository;
    private readonly IStockTransactionRepository stockTransactionRepository;
    private readonly IShopRepository shopRepository;
    private readonly IInvoiceNumberAllocator invoiceNumberAllocator;
    private readonly IUnitOfWork unitOfWork;
    private readonly ICustomerRepository customerRepository;
    private readonly IUserRepository userRepository;

    public RecordSaleCommandHandler(
        ISaleLineValidator saleLineValidator,
        ISalePricingCalculator salePricingCalculator,
        ICustomerResolver customerResolver,
        ISaleRepository saleRepository,
        ICreditNoteRepository creditNoteRepository,
        SaleDtoBuilder saleDtoBuilder,
        ICustomerLedgerEntryRepository customerLedgerEntryRepository,
        IStockTransactionRepository stockTransactionRepository,
        IShopRepository shopRepository,
        IInvoiceNumberAllocator invoiceNumberAllocator,
        IUnitOfWork unitOfWork,
        ICustomerRepository customerRepository,
        IUserRepository userRepository)
    {
        this.saleLineValidator = saleLineValidator;
        this.salePricingCalculator = salePricingCalculator;
        this.customerResolver = customerResolver;
        this.saleRepository = saleRepository;
        this.creditNoteRepository = creditNoteRepository;
        this.saleDtoBuilder = saleDtoBuilder;
        this.customerLedgerEntryRepository = customerLedgerEntryRepository;
        this.stockTransactionRepository = stockTransactionRepository;
        this.shopRepository = shopRepository;
        this.invoiceNumberAllocator = invoiceNumberAllocator;
        this.unitOfWork = unitOfWork;
        this.customerRepository = customerRepository;
        this.userRepository = userRepository;
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

            var existingRedemptions = await LoadCreditNoteRedemptionsAsync(existingSale.ShopId, existingSale.Id, cancellationToken);
            if (existingRedemptions.IsError)
                return existingRedemptions.Errors;

            return await saleDtoBuilder.BuildSaleDtoAsync(existingSale, existingSale.Warnings, existingRedemptions.Value, cancellationToken);
        }

        var validationResultOrError = await saleLineValidator.ValidateLinesAsync(command.ShopId, command.Items, warnings, cancellationToken);
        if (validationResultOrError.IsError)
            return validationResultOrError.Errors;

        var validatedLines = validationResultOrError.Value.Lines;
        var itemById = validatedLines
            .Where(l => l.Item is not null)
            .Select(l => l.Item!)
            .DistinctBy(i => i.Id)
            .ToDictionary(i => i.Id);
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
                line.LineType == SaleLineType.Goods ? line.Batch!.TaxIncluded : line.Command.IsPriceIncludingTax,
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
        var creditNoteRedemptions = command.CreditNoteRedemptions ?? [];
        if (command.CreditNoteAppliedAmount > 0m && creditNoteRedemptions.Count == 0)
            return Errors.Sale.CreditNoteRedemptionsSplitMismatch;

        var requestedCreditNoteAppliedAmount = command.CreditNoteAppliedAmount;
        var creditNoteAppliedAmount = creditNoteRedemptions.Count > 0
            ? creditNoteRedemptions.Sum(redemption => redemption.Amount)
            : requestedCreditNoteAppliedAmount;
        if (requestedCreditNoteAppliedAmount > 0m && creditNoteAppliedAmount != requestedCreditNoteAppliedAmount)
        {
            return Errors.Sale.CreditNoteRedemptionsSplitMismatch;
        }

        // Refused before a number is taken: an undeclared or unsupported profile issues nothing.
        var shop = await shopRepository.GetByIdAsync(command.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var sellerProfileOrError = InvoiceDocumentBuilder.CaptureSellerProfile(shop);
        if (sellerProfileOrError.IsError)
            return sellerProfileOrError.Errors;

        var sellerProfile = sellerProfileOrError.Value;
        var fiscalYear = FiscalYear.ForDate(soldAt);
        var series = new InvoiceSeriesKey(shop.Id, shop.SellerRegistrationKey, sellerProfile.SeriesCode, fiscalYear);

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

        var transactionStarted = false;
        var transactionCommitted = false;
        var transactionRolledBack = false;

        try
        {
            // The number is taken inside the posting transaction: the series row stays locked until
            // the sale commits, and any refusal below rolls the counter back with everything else.
            await unitOfWork.BeginTransactionAsync(cancellationToken);
            transactionStarted = true;

            // The customer is the first lock, as for payments and returns: the credit decision is
            // made on the balance and settings committed now, and a second due sale for the same
            // customer waits here until this one commits or rolls back.
            CustomerCreditCheck? creditCheck = null;
            if (command.DueAmount > 0m && resolvedCustomer is not null)
            {
                var creditOrError = await DecideCreditAsync(command, resolvedCustomer.Id, cancellationToken);
                if (creditOrError.IsError)
                    return creditOrError.Errors;

                if (creditOrError.Value.Replay is { } replay)
                    return replay;

                creditCheck = creditOrError.Value;
                if (creditCheck.Decision.ExceedsAdvisoryLimit)
                    warnings.Add($"Customer due {creditCheck.Decision.ExposureAfter:0.00} exceeds the advisory credit limit {creditCheck.Decision.CreditLimit:0.00} (not enforced).");
            }

            var invoiceNumberOrError = await invoiceNumberAllocator.AllocateAsync(series, cancellationToken);
            if (invoiceNumberOrError.IsError)
                return invoiceNumberOrError.Errors;

            var invoiceNumber = invoiceNumberOrError.Value;
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
                new BusinessDay(Domain.Common.BusinessCalendar.DateOf(soldAt, Domain.Common.BusinessTimeZones.Resolve(shop.BusinessTimeZoneId)), shop.BusinessTimeZoneId),
                pricing.ConfiguredSaleRule?.RuleId,
                pricing.ConfiguredSaleRule?.RuleType,
                pricing.ConfiguredSaleRule?.Percentage,
                pricing.ConfiguredSaleRule?.ThresholdAmount,
                effectiveSaleDiscount.Type,
                effectiveSaleDiscount.Value,
                warnings,
                creditNoteAppliedAmount);
            if (saleOrError.IsError)
                return saleOrError.Errors;

            var sale = saleOrError.Value;
            var documentOrError = InvoiceDocumentBuilder.Build(
                new InvoiceIssueContext(sellerProfile, resolvedCustomer, command.PlaceOfSupplyStateCode, soldAt, fiscalYear, ValidatedSaleLines.DocumentUnitsOf(validatedLines)),
                sale);
            if (documentOrError.IsError)
                return documentOrError.Errors;

            sale.AttachInvoiceDocument(documentOrError.Value);
            if (creditCheck is not null)
            {
                sale.ApplyCreditTerms(creditCheck.CreditTermsDays);
                if (creditCheck.Decision is { Kind: CreditDecisionKind.Overridden, Override: { } approved })
                    sale.RecordCreditOverride(approved.Reason, command.ActorUserId, approved.ObservedExposure, creditCheck.Decision.CreditLimit);
            }

            var creditNoteRedemptionsOrError = await RedeemCreditNotesAsync(
                command.ShopId,
                sale.Id,
                creditNoteRedemptions,
                resolvedCustomer?.Id ?? command.CustomerId,
                command.CreditNoteCustomerMismatchConfirmed,
                cancellationToken);
            if (creditNoteRedemptionsOrError.IsError)
            {
                var replayResult = await TryGetReplayForIdempotentSaleAsync(
                    command.ShopId,
                    command.ActorUserId,
                    normalizedIdempotencyKey,
                    requestHash,
                    cancellationToken);

                if (replayResult.HasValue)
                    return replayResult.Value;

                return creditNoteRedemptionsOrError.Errors;
            }

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

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionCommitted = true;

            return saleDtoBuilder.BuildSaleDto(sale, itemById, sale.Warnings, creditNoteRedemptionsOrError.Value);
        }
        catch (DbUpdateConcurrencyException exception) when (IsCreditNoteConcurrencyConflict(command, exception))
        {
            return Errors.Sale.CreditNoteRedemptionConflict;
        }
        catch (DbUpdateException)
        {
            if (transactionStarted && !transactionCommitted)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                transactionRolledBack = true;
            }

            var replayResult = await TryGetReplayForIdempotentSaleAsync(
                command.ShopId,
                command.ActorUserId,
                normalizedIdempotencyKey,
                requestHash,
                cancellationToken);

            if (replayResult.HasValue)
                return replayResult.Value;

            throw;
        }
        finally
        {
            if (transactionStarted && !transactionCommitted && !transactionRolledBack)
            {
                await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }
        }
    }

    private sealed record CustomerCreditCheck(CreditDecision Decision, int? CreditTermsDays, ErrorOr<SaleDto>? Replay = null);

    /// <summary>
    /// Locks the customer, then judges the new due on what is committed now. A retry of a sale that
    /// committed while this request waited for the lock replays it instead of being judged again.
    /// </summary>
    private async Task<ErrorOr<CustomerCreditCheck>> DecideCreditAsync(
        RecordSaleCommand command,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var lockOutcome = await customerRepository.LockForCreditAsync(command.ShopId, customerId, cancellationToken);
        switch (lockOutcome)
        {
            case RowLockOutcome.Acquired:
                break;
            case RowLockOutcome.TimedOut:
                return Errors.CustomerCredit.Busy;
            default:
                return Errors.Sale.CreditCustomerNotFound;
        }

        var normalizedKey = command.IdempotencyKey.Trim();
        var replay = await TryGetReplayForIdempotentSaleAsync(
            command.ShopId,
            command.ActorUserId,
            normalizedKey,
            RecordSaleIdempotencyHasher.ComputeHash(command),
            cancellationToken);
        if (replay.HasValue)
            return new CustomerCreditCheck(new CreditDecision(CreditDecisionKind.NoNewDue, 0m, 0m, 0m), null, replay.Value);

        var customer = await customerRepository.GetCurrentAsync(command.ShopId, customerId, cancellationToken);
        if (customer is null)
            return Errors.Sale.CreditCustomerNotFound;

        var exposure = await customerLedgerEntryRepository.GetCustomerBalanceAsync(command.ShopId, customerId, cancellationToken);

        ShopRole? actorRole = null;
        if (command.CreditOverride is not null)
        {
            var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
            actorRole = actor?.ShopMemberships.FirstOrDefault(m => m.ShopId == command.ShopId)?.Role;
        }

        var decision = CustomerCreditPolicy.Evaluate(
            CustomerCreditState.Of(customer),
            exposure,
            command.DueAmount,
            command.CreditOverride,
            actorRole);
        if (decision.IsError)
            return decision.Errors;

        return new CustomerCreditCheck(decision.Value, customer.CreditTermsDays);
    }

    private async Task<ErrorOr<IReadOnlyList<SaleCreditNoteRedemptionSummaryDto>>> LoadCreditNoteRedemptionsAsync(
        Guid shopId,
        Guid saleId,
        CancellationToken cancellationToken)
    {
        var redemptions = await creditNoteRepository.GetRedemptionsBySaleIdAsync(shopId, saleId, cancellationToken) ?? [];

        return redemptions.Select(redemption =>
            new SaleCreditNoteRedemptionSummaryDto(
                redemption.CreditNoteId,
                redemption.Code,
                redemption.Amount)).ToList();
    }

    private async Task<ErrorOr<IReadOnlyList<SaleCreditNoteRedemptionSummaryDto>>> RedeemCreditNotesAsync(
        Guid shopId,
        Guid saleId,
        IReadOnlyList<CreditNoteRedemptionCommand> creditNoteRedemptions,
        Guid? saleCustomerId,
        bool customerMismatchConfirmed,
        CancellationToken cancellationToken)
    {
        if (creditNoteRedemptions.Count == 0)
        {
            return Array.Empty<SaleCreditNoteRedemptionSummaryDto>();
        }

        var summaries = new List<SaleCreditNoteRedemptionSummaryDto>(creditNoteRedemptions.Count);
        foreach (var redemption in creditNoteRedemptions)
        {
            var creditNote = await creditNoteRepository.GetByCodeForUpdateWithRedemptionsAsync(
                shopId,
                redemption.Code,
                cancellationToken);
            if (creditNote is null)
            {
                return Errors.CreditNote.CreditNoteNotFound(redemption.Code);
            }

            if (creditNote.LinkedCustomerId.HasValue && creditNote.LinkedCustomerId.Value != saleCustomerId && !customerMismatchConfirmed)
            {
                return Errors.CreditNote.CustomerMismatchRequiresConfirmation;
            }

            var redeemResult = creditNote.Redeem(shopId, saleId, redemption.Amount);
            if (redeemResult.IsError)
            {
                return redeemResult.Errors;
            }

            await creditNoteRepository.AddRedemptionAsync(redeemResult.Value, cancellationToken);
            summaries.Add(new SaleCreditNoteRedemptionSummaryDto(creditNote.Id, creditNote.Code, redemption.Amount));
        }

        return summaries;
    }

    private async Task<ErrorOr<SaleDto>?> TryGetReplayForIdempotentSaleAsync(
        Guid shopId,
        Guid actorUserId,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var concurrentSale = await saleRepository.GetByIdempotencyKeyAsync(shopId, actorUserId, idempotencyKey, cancellationToken);
        if (concurrentSale is null)
            return null;

        if (!string.Equals(concurrentSale.RequestHash, requestHash, StringComparison.Ordinal))
            return Errors.Sale.IdempotencyConflict;

        var concurrentRedemptions = await LoadCreditNoteRedemptionsAsync(concurrentSale.ShopId, concurrentSale.Id, cancellationToken);
        if (concurrentRedemptions.IsError)
            return concurrentRedemptions.Errors;

        return await saleDtoBuilder.BuildSaleDtoAsync(concurrentSale, concurrentSale.Warnings, concurrentRedemptions.Value, cancellationToken);
    }

    private static bool IsCreditNoteConcurrencyConflict(
        RecordSaleCommand command,
        DbUpdateConcurrencyException exception) =>
        command.CreditNoteRedemptions is { Count: > 0 }
        && (exception.Entries.Count == 0 || exception.Entries.Any(entry => entry.Entity is CreditNote));
}
