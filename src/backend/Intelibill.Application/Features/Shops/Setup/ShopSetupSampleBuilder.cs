using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Application.Features.Sales.Services.Pricing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Shops.Setup;

internal sealed record ShopSetupCandidates(ShopSetupSampleDto? Sample, bool HasUsableStock,
    string? StockReason, string? CatalogReason);

/// <summary>Evaluates a saved catalog line for a walk-in cash sale without posting a sale or taking a number.</summary>
public sealed class ShopSetupSampleBuilder(
    IShopSetupReadRepository repository,
    IUnitOfWork unitOfWork,
    IExpiryPolicyFence expiryFence,
    SaleExpiryGuard expiryGuard,
    IDiscountRuleRepository discountRules,
    IBusinessClock clock)
{
    private const int PageSize = 32;
    private static readonly InstantDiscount NoDiscount = new(InstantDiscountType.None, 0m);

    internal async Task<ShopSetupCandidates> EvaluateAsync(Shop shop, ShopSetupFacts facts, InvoiceSellerProfile? profile, Guid actor, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var date = clock.GetBusinessDay(now, shop.BusinessTimeZoneId);
        // Each request captures the rules when its first candidate reaches pricing.
        var rules = new Lazy<Task<SalePricingCalculator.RuleSnapshot>>(async () =>
            SalePricingCalculator.CaptureRules(await discountRules.GetActiveByShopAsync(shop.Id, now, ct)));
        var context = new SampleContext(shop.Id, actor, profile, date, now, rules);
        var services = facts.HasActiveServices && profile is not null
            ? await FindServiceAsync(context, ct)
            : new ShopSetupCandidates(null, false, null, null);
        if (!facts.HasActiveGoods)
            return services;

        var goods = await PostingTransaction.RunAsync<ShopSetupCandidates>(unitOfWork, async token =>
        {
            var locked = await expiryFence.AcquireAsync(shop.Id, false, token);
            if (locked.IsError)
                return locked.Errors;

            return await FindGoodsAsync(context, services.Sample is null, token);
        }, ct);
        if (goods.IsError)
            return services with { StockReason = goods.FirstError.Code };

        return new(services.Sample ?? goods.Value.Sample, goods.Value.HasUsableStock, goods.Value.StockReason,
            services.CatalogReason ?? goods.Value.CatalogReason);
    }

    private async Task<ShopSetupCandidates> FindServiceAsync(SampleContext context, CancellationToken ct)
    {
        string? reason = null;
        for (var offset = 0; ; offset += PageSize)
        {
            var services = await repository.GetServicesAsync(context.ShopId, offset, PageSize, ct);
            foreach (var service in services)
            {
                var line = new SalePricingLineCalculationRequest(SaleLineType.Service, Guid.Empty, service.Id,
                    1m, 0m, service.Price, service.Price, service.TaxRatePercent, service.TaxIncluded, NoDiscount, false);
                var sample = await BuildAsync(context, line, service.Name, service.Code, service.HsnCode, null, ct);
                if (!sample.IsError)
                    return new(sample.Value, false, null, null);
                reason ??= sample.FirstError.Code;
            }

            if (services.Count < PageSize)
                return new(null, false, null, reason);
        }
    }

    private async Task<ShopSetupCandidates> FindGoodsAsync(SampleContext context, bool needSample, CancellationToken ct)
    {
        var hasUsableStock = false;
        string? stockReason = null;
        string? catalogReason = null;
        for (var offset = 0; ; offset += PageSize)
        {
            var goods = await repository.GetGoodsAsync(context.ShopId, offset, PageSize, ct);
            if (goods.Count == 0)
                break;

            var expiry = await expiryGuard.BeginEvaluationAsync(context.ShopId, context.Day.Date, goods.Select(candidate => candidate.Item.Id), ct);
            foreach (var candidate in goods)
            {
                var stock = ResolveQuantity(candidate, expiry.EvaluateBatch(candidate.Batch));
                if (stock.IsError)
                {
                    stockReason ??= stock.FirstError.Code;
                    continue;
                }

                hasUsableStock = true;
                if (!needSample || context.Profile is null)
                    return new(null, true, null, null);

                var (item, batch, _) = candidate;
                var line = new SalePricingLineCalculationRequest(batch.Id, stock.Value, batch.GetProfitCostPrice(),
                    batch.SalesPrice, batch.Mrp, batch.TaxRatePercent, batch.TaxIncluded, NoDiscount);
                var sample = await BuildAsync(context, line, item.Name, item.Barcode, item.HsnCode, item, ct);
                if (!sample.IsError)
                    return new(sample.Value, true, null, null);
                catalogReason ??= sample.FirstError.Code;
            }

            if (goods.Count < PageSize)
                break;
        }

        return new(null, hasUsableStock, hasUsableStock ? null : stockReason ?? "ShopSetup.OpeningStockRequired", catalogReason);
    }

    private static ErrorOr<decimal> ResolveQuantity(ShopSetupGoodsCandidate candidate, SellabilityDecision decision)
    {
        var (item, batch, aggregate) = candidate;
        if (batch.UnquarantinedQuantity == 0m && batch.QuarantinedQuantity > 0m)
            return Error.Conflict("Expiry.Quarantined", "All stock in this lot is quarantined.");
        if (!decision.Allowed)
            return Error.Conflict(decision.ReasonCode!, "Current expiry policy blocks this lot.");
        if (decision.RequiresApproval)
            return Error.Conflict("Expiry.ApprovalRequired", "This lot needs an expiry warning approval.");
        var catalogUnit = QuantityUnits.Resolve(item.Uom);
        if (item.UnitReviewRequired || string.IsNullOrWhiteSpace(item.Uom) || item.QuantityScale is < 0 or > QuantityUnits.MaxScale
            || item.UnitCode != catalogUnit?.Code || (catalogUnit is not null && item.QuantityScale != catalogUnit.Scale))
            return Error.Conflict("ShopSetup.UnitReviewRequired", "Review the item's unit and quantity precision.");

        var quantity = decimal.Round(Math.Min(decision.AvailableQuantity, 1m), item.QuantityScale, MidpointRounding.ToZero);
        if (quantity <= 0m)
            return Errors.Sale.QuantityPrecisionExceeded(item.Barcode, item.Uom, item.QuantityScale);
        if (aggregate is null)
            return Errors.Sale.InventoryAggregateNotFound(item.Barcode);
        if (aggregate < quantity)
            return Error.Conflict("ShopSetup.AggregateStockInsufficient", "The item's stock summary cannot cover this quantity.");
        return quantity;
    }

    private static async Task<ErrorOr<ShopSetupSampleDto>> BuildAsync(SampleContext context, SalePricingLineCalculationRequest request,
        string name, string code, string? hsn, Item? item, CancellationToken ct)
    {
        if (!SaleLineValidator.IsValidTaxRatePercent(request.TaxRatePercent))
            return Errors.Sale.InvalidTaxRatePercent;
        if (!string.IsNullOrWhiteSpace(hsn) && !SaleLineValidator.IsValidHsnCode(hsn))
            return Errors.Sale.InvalidHsnCode;

        var rules = await context.DiscountRules.Value;
        ct.ThrowIfCancellationRequested();
        var priced = SalePricingCalculator.Calculate(new SalePricingCalculationRequest(context.ShopId, context.Now, [request], NoDiscount), rules);
        if (priced.IsError)
            return priced.Errors;

        var totals = priced.Value;
        var line = totals.Lines[0];
        var input = new SaleLineInput(context.ShopId, line.LineType, item?.Id, item is null ? null : line.InventoryBatchId,
            line.ServiceId, name, code, line.Quantity, line.CostPrice, line.SalesPrice, request.Mrp, line.TaxRatePercent,
            line.IsPriceIncludingTax, HasPriceMismatch: false, line.PreTaxAmountBeforeDiscount, line.ItemDiscountAmount,
            line.SaleDiscountAmount, line.TaxableAmount, line.TaxAmount, line.TotalAmount,
            line.ConfiguredBatchRuleId, line.ConfiguredBatchRulePercentage, HsnCode: hsn);

        var configured = totals.ConfiguredSaleRule;
        var sale = Sale.Record(context.ShopId, context.Actor, "SAMPLE", "SAMPLE", "SAMPLE", [input],
            null, null, null, PaymentMethod.Cash, totals.TotalAmount, 0m, context.Now, context.Day,
            configured?.RuleId, configured?.RuleType, configured?.Percentage, configured?.ThresholdAmount);
        if (sale.IsError)
            return sale.Errors;

        var units = item is null ? null : new Dictionary<Guid, string> { [item.Id] = item.DocumentUnit };
        var document = InvoiceDocumentBuilder.Build(
            new InvoiceIssueContext(context.Profile!, null, null, context.Now, FiscalYear.ForBusinessDate(context.Day.Date), units, context.Day.Date),
            sale.Value);
        if (document.IsError)
            return document.Errors;

        return new ShopSetupSampleDto(true, document.Value with { Number = string.Empty }, line.LineType, context.Now);
    }

    private sealed record SampleContext(Guid ShopId, Guid Actor, InvoiceSellerProfile? Profile, BusinessDay Day, DateTimeOffset Now,
        Lazy<Task<SalePricingCalculator.RuleSnapshot>> DiscountRules);
}
