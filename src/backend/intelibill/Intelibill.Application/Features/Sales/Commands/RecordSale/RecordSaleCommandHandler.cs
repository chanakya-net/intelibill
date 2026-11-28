using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Expiry;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Features.Customers.Services;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Application.Features.Sales.Services.Pricing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Intelibill.Application.Features.Tills;

using CustomerCreditCheck = Intelibill.Application.Features.Sales.Commands.RecordSale.SaleCreditDecider.CustomerCreditCheck;
using SalePostingContext = Intelibill.Application.Features.Sales.Commands.RecordSale.SalePostingContextReader.SalePostingContext;
using ValidatedSaleItems = Intelibill.Application.Features.Sales.Commands.RecordSale.SaleLinePricer.ValidatedSaleItems;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

public sealed class RecordSaleCommandHandler
{
    private readonly ICustomerResolver customerResolver;
    private readonly ISaleRepository saleRepository;
    private readonly SaleDtoBuilder saleDtoBuilder;
    private readonly IInvoiceNumberAllocator invoiceNumberAllocator;
    private readonly IUnitOfWork unitOfWork;
    private readonly IPostingPeriodPolicy postingPeriodPolicy;
    private readonly ITillPostingService tillPosting;
    private readonly IExpiryPolicyFence expiryFence;
    private readonly IShopLicenceFence licenceFence;
    private readonly ILiveSessionAuthority sessionAuthority;
    private readonly SaleReplayReader replayReader;
    private readonly SalePostingContextReader postingContextReader;
    private readonly SaleLinePricer saleLinePricer;
    private readonly SaleCreditDecider creditDecider;
    private readonly SaleCreditNoteRedeemer creditNoteRedeemer;
    private readonly SaleStockAndDuePoster stockAndDuePoster;

    public RecordSaleCommandHandler(
        ICustomerResolver customerResolver,
        ISaleRepository saleRepository,
        SaleDtoBuilder saleDtoBuilder,
        IInvoiceNumberAllocator invoiceNumberAllocator,
        IUnitOfWork unitOfWork,
        IPostingPeriodPolicy postingPeriodPolicy,
        ITillPostingService tillPosting,
        IExpiryPolicyFence expiryFence,
        IShopLicenceFence licenceFence,
        ILiveSessionAuthority sessionAuthority,
        SaleReplayReader replayReader,
        SalePostingContextReader postingContextReader,
        SaleLinePricer saleLinePricer,
        SaleCreditDecider creditDecider,
        SaleCreditNoteRedeemer creditNoteRedeemer,
        SaleStockAndDuePoster stockAndDuePoster)
    {
        this.customerResolver = customerResolver;
        this.saleRepository = saleRepository;
        this.saleDtoBuilder = saleDtoBuilder;
        this.invoiceNumberAllocator = invoiceNumberAllocator;
        this.unitOfWork = unitOfWork;
        this.postingPeriodPolicy = postingPeriodPolicy;
        this.tillPosting = tillPosting;
        this.expiryFence = expiryFence;
        this.licenceFence = licenceFence;
        this.sessionAuthority = sessionAuthority;
        this.replayReader = replayReader;
        this.postingContextReader = postingContextReader;
        this.saleLinePricer = saleLinePricer;
        this.creditDecider = creditDecider;
        this.creditNoteRedeemer = creditNoteRedeemer;
        this.stockAndDuePoster = stockAndDuePoster;
    }

    [LicenceAction(LicenceAction.NewTrading)]
    public async Task<ErrorOr<SaleDto>> HandleAsync(RecordSaleCommand command, CancellationToken cancellationToken)
    {
        // A saved outcome is recoverable after close/disablement, but never after losing shop authority.
        if (command.ShopId == Guid.Empty || !await sessionAuthority.IsValidAsync(command.Session, cancellationToken))
            return Errors.Auth.SessionInvalidated;
        var warnings = new List<string>();
        var normalizedIdempotencyKey = command.IdempotencyKey.Trim();
        var requestHash = RecordSaleIdempotencyHasher.ComputeHash(command);

        var existing = await replayReader.TryGetReplayForIdempotentSaleAsync(
            command, normalizedIdempotencyKey, requestHash, cancellationToken);
        if (existing.HasValue)
            return existing.Value;

        var transactionStarted = false;
        var transactionCommitted = false;
        var transactionRolledBack = false;

        try
        {
            // The number is taken inside the posting transaction: the series row stays locked until
            // the sale commits, and any refusal below rolls the counter back with everything else.
            await unitOfWork.BeginTransactionAsync(cancellationToken);
            transactionStarted = true;
            await saleRepository.LockIdempotencyKeyAsync(command.ShopId, command.ActorUserId, normalizedIdempotencyKey, cancellationToken);
            var committed = await replayReader.RecheckSessionThenReplayAsync(command, normalizedIdempotencyKey, requestHash, cancellationToken);
            if (committed.HasValue)
                return committed.Value;

            var contextOrError = await postingContextReader.CapturePostingContextAsync(command, cancellationToken);
            if (contextOrError.IsError)
                return contextOrError.Errors;
            var context = contextOrError.Value;

            if (await licenceFence.ReadTradingDenialAsync(command.ShopId, cancellationToken) is { } denied)
                return denied;

            // A closed month refuses posting before a credit lock or invoice number is taken.
            // The period's shared lock is held until commit.
            var periodOrError = await postingPeriodPolicy.AuthorizeForWriteAsync(
                command.ShopId,
                context.BusinessDay.Date,
                cancellationToken);
            if (periodOrError.IsError)
                return periodOrError.Errors;

            var expiryLock = await expiryFence.AcquireAsync(command.ShopId, false, cancellationToken);
            if (expiryLock.IsError) return expiryLock.Errors;

            var tillOrError = await AcquireTillAndValidateTenderAsync(command, context.BusinessDay.Date, cancellationToken);
            if (tillOrError.IsError) return tillOrError.Errors;
            var tillSession = tillOrError.Value;

            var linesOrError = await saleLinePricer.ValidateLinesAndExpiryAsync(command, context.BusinessDay.Date, warnings, cancellationToken);
            if (linesOrError.IsError)
                return linesOrError.Errors;
            var lines = linesOrError.Value;

            var effectiveSaleDiscount = command.SaleDiscount ?? new InstantDiscount(InstantDiscountType.None, 0m);
            var pricingOrError = await saleLinePricer.PriceLinesAsync(
                command,
                context.SoldAt,
                lines.ValidatedLines,
                effectiveSaleDiscount,
                cancellationToken);
            if (pricingOrError.IsError)
                return pricingOrError.Errors;
            var pricing = pricingOrError.Value;

            var buyerOrError = await ResolveBuyerAsync(command, cancellationToken);
            if (buyerOrError.IsError)
                return buyerOrError.Errors;
            var buyer = buyerOrError.Value;
            var resolvedCustomer = buyer.Customer;

            var saleLineInputs = RecordSaleLineMapping.BuildSaleLineInputs(command, lines.ValidatedLines, pricing);

            // After the period and till locks, the customer is the first business lock: the credit decision is
            // made on the balance and settings committed now, and a second due sale for the same
            // customer waits here until this one commits or rolls back.
            CustomerCreditCheck? creditCheck = null;
            if (command.DueAmount > 0m && resolvedCustomer is not null)
            {
                var creditOrError = await creditDecider.LockCustomerAndDecideCreditAsync(command, resolvedCustomer.Id, cancellationToken);
                if (creditOrError.IsError)
                    return creditOrError.Errors;

                if (creditOrError.Value.Replay is { } replay)
                    return replay;

                creditCheck = creditOrError.Value;
            }

            var invoiceNumberOrError = await invoiceNumberAllocator.AllocateAsync(context.Series, cancellationToken);
            if (invoiceNumberOrError.IsError)
                return invoiceNumberOrError.Errors;

            var invoiceNumber = invoiceNumberOrError.Value;
            var saleOrError = IssueSale(
                command,
                normalizedIdempotencyKey,
                requestHash,
                invoiceNumber,
                saleLineInputs,
                buyer,
                context,
                lines,
                pricing,
                effectiveSaleDiscount,
                warnings,
                creditCheck);
            if (saleOrError.IsError)
                return saleOrError.Errors;
            var sale = saleOrError.Value;

            var creditNoteRedemptionsOrError = await creditNoteRedeemer.LockAndRedeemCreditNotesAsync(
                command.ShopId,
                sale.Id,
                buyer.CreditNoteRedemptions,
                resolvedCustomer?.Id ?? command.CustomerId,
                command.CreditNoteCustomerMismatchConfirmed,
                cancellationToken);
            if (creditNoteRedemptionsOrError.IsError)
            {
                return await replayReader.ReplayOrRefuseAsync(
                    command,
                    normalizedIdempotencyKey,
                    requestHash,
                    creditNoteRedemptionsOrError.Errors,
                    cancellationToken);
            }

            var stockOrError = await stockAndDuePoster.DeductStockAsync(command, lines.ValidatedLines, invoiceNumber, context.SoldAt, cancellationToken);
            if (stockOrError.IsError)
                return stockOrError.Errors;

            await saleRepository.AddAsync(sale, cancellationToken);
            var dueOrError = await stockAndDuePoster.PostCustomerDueAsync(command, sale, cancellationToken);
            if (dueOrError.IsError)
                return dueOrError.Errors;

            var tillPostingOrError = await AppendTillSettlementAsync(
                command,
                tillSession,
                sale,
                normalizedIdempotencyKey,
                cancellationToken);
            if (tillPostingOrError.IsError)
                return tillPostingOrError.Errors;

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionCommitted = true;

            return saleDtoBuilder.BuildSaleDto(sale, lines.ItemById, sale.Warnings, creditNoteRedemptionsOrError.Value);
        }
        catch (Exception exception) when (unitOfWork.IsConcurrencyConflict(exception) && IsCreditNoteConcurrencyConflict(command, exception))
        {
            return Errors.Sale.CreditNoteRedemptionConflict;
        }
        catch (Exception exception) when (unitOfWork.IsWriteFailure(exception))
        {
            if (transactionStarted && !transactionCommitted)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                transactionRolledBack = true;
            }

            var replayResult = await replayReader.TryGetReplayForIdempotentSaleAsync(
                command,
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

    private sealed record SaleBuyer(
        Customer? Customer,
        string? NormalizedCustomerPhone,
        IReadOnlyList<CreditNoteRedemptionCommand> CreditNoteRedemptions,
        decimal CreditNoteAppliedAmount);

    /// <summary>Returns the acquisition result itself, so a disabled till still reads as a null session.</summary>
    private async Task<ErrorOr<TillSession?>> AcquireTillAndValidateTenderAsync(
        RecordSaleCommand command,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        // Acquisition is local to this transaction. Every new attempt reacquires/reloads; never
        // reuse the posting service's cached tracked session after commit or rollback.
        var tillOrError = await tillPosting.AcquireAsync(command.ShopId, command.ActorUserId, command.Till, businessDate, cancellationToken);
        if (tillOrError.IsError) return tillOrError.Errors;
        var tillSession = tillOrError.Value;
        if (tillSession is null && command.TenderLines is not null) return Errors.Till.Disabled;
        if (tillSession is not null)
        {
            var tenderCheck = SaleTenderPolicy.Validate(command.PaymentMethod, command.PaidAmount, command.TenderLines);
            if (tenderCheck.IsError) return tenderCheck.Errors;
        }

        return tillOrError;
    }

    private async Task<ErrorOr<SaleBuyer>> ResolveBuyerAsync(
        RecordSaleCommand command,
        CancellationToken cancellationToken)
    {
        var normalizedCustomerPhone = string.IsNullOrWhiteSpace(command.CustomerPhone) ? null : command.CustomerPhone.Trim();
        var resolvedCustomerOrError = await customerResolver.ResolveAsync(
            command.ShopId,
            command.CustomerId,
            command.CustomerPhone,
            command.DueAmount > 0,
            command.PaymentMethod,
            cancellationToken);
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

        return new SaleBuyer(resolvedCustomer, normalizedCustomerPhone, creditNoteRedemptions, creditNoteAppliedAmount);
    }

    /// <summary>Records the sale aggregate and freezes its expiry evidence, invoice document and credit terms.</summary>
    private static ErrorOr<Sale> IssueSale(
        RecordSaleCommand command,
        string normalizedIdempotencyKey,
        string requestHash,
        string invoiceNumber,
        List<SaleLineInput> saleLineInputs,
        SaleBuyer buyer,
        SalePostingContext context,
        ValidatedSaleItems lines,
        SalePricingCalculationResult pricing,
        InstantDiscount effectiveSaleDiscount,
        List<string> warnings,
        CustomerCreditCheck? creditCheck)
    {
        var resolvedCustomer = buyer.Customer;
        var saleOrError = Sale.Record(
            command.ShopId,
            command.ActorUserId,
            normalizedIdempotencyKey,
            requestHash,
            invoiceNumber,
            saleLineInputs,
            resolvedCustomer?.Id ?? command.CustomerId,
            resolvedCustomer?.Name ?? command.CustomerName,
            resolvedCustomer?.PhoneNumber ?? buyer.NormalizedCustomerPhone,
            command.PaymentMethod,
            command.PaidAmount,
            command.DueAmount,
            context.SoldAt,
            context.BusinessDay,
            pricing.ConfiguredSaleRule?.RuleId,
            pricing.ConfiguredSaleRule?.RuleType,
            pricing.ConfiguredSaleRule?.Percentage,
            pricing.ConfiguredSaleRule?.ThresholdAmount,
            effectiveSaleDiscount.Type,
            effectiveSaleDiscount.Value,
            warnings,
            buyer.CreditNoteAppliedAmount);
        if (saleOrError.IsError)
            return saleOrError.Errors;

        var sale = saleOrError.Value;
        sale.AttachExpiryDecisions(lines.ExpiryDecisions);
        var issueContext = new InvoiceIssueContext(
            context.SellerProfile,
            resolvedCustomer,
            command.PlaceOfSupplyStateCode,
            context.SoldAt,
            context.FiscalYear,
            ValidatedSaleLines.DocumentUnitsOf(lines.ValidatedLines),
            context.BusinessDay.Date);
        var documentOrError = InvoiceDocumentBuilder.Build(issueContext, sale);
        if (documentOrError.IsError)
            return documentOrError.Errors;

        sale.AttachInvoiceDocument(documentOrError.Value);
        if (creditCheck is not null)
        {
            sale.ApplyCreditTerms(creditCheck.CreditTermsDays);
            if (creditCheck.Decision is { Kind: CreditDecisionKind.Overridden, Override: { } approved })
                sale.RecordCreditOverride(approved.Reason, command.ActorUserId, approved.ObservedExposure, creditCheck.Decision.CreditLimit);
        }

        return sale;
    }

    private async Task<ErrorOr<Success>> AppendTillSettlementAsync(
        RecordSaleCommand command,
        TillSession? tillSession,
        Sale sale,
        string normalizedIdempotencyKey,
        CancellationToken cancellationToken)
    {
        if (tillSession is not null)
        {
            var evidence = sale.AttachTillSettlement(tillSession.Id, command.Till?.OverrideReason, command.TenderLines ?? []);
            if (evidence.IsError) return evidence.Errors;
            var source = new TillSourcePosting(
                TillMovementSource.OnlineSale,
                sale.Id,
                "sale",
                sale.TenderLines!,
                1m,
                sale.SoldAt,
                normalizedIdempotencyKey,
                null,
                StoreCreditRedeemed: sale.CreditNoteAppliedAmount,
                ReceivableChange: sale.DueAmount);
            var appended = await tillPosting.AppendAsync(tillSession, source, command.ActorUserId, cancellationToken);
            if (appended.IsError) return appended.Errors;
        }

        return Result.Success;
    }

    private bool IsCreditNoteConcurrencyConflict(
        RecordSaleCommand command,
        Exception exception)
    {
        if (command.CreditNoteRedemptions is not { Count: > 0 })
            return false;

        var entities = unitOfWork.ConflictedEntities(exception);
        return entities.Count == 0 || entities.Any(entity => entity is CreditNote);
    }
}
