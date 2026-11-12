using ErrorOr;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Shops.Setup;

public sealed class ShopSetupService(
    IShopSetupReadRepository readRepository,
    IShopSetupPreferencesRepository preferenceRepository,
    ShopSetupSampleBuilder sampleBuilder)
{
    public async Task<ErrorOr<ShopSetupStatusDto>> GetAsync(GetShopSetupQuery query, CancellationToken ct)
    {
        var evaluation = await EvaluateAsync(query.ShopId, query.ActorUserId, query.ActiveShopId, ct);
        if (evaluation.IsError)
            return evaluation.Errors;

        var result = evaluation.Value;
        var preferences = ShopSetupPreferencesDto.From(await preferenceRepository.GetAsync(query.ShopId, ct));
        var owner = result.Role == ShopRole.Owner;
        return new ShopSetupStatusDto(query.ShopId, 1, result.CanTrade,
            [
                RequiredStep("identity", result.IdentityReason),
                RequiredStep("business", result.BusinessReason),
                RequiredStep("tax", result.TaxReason),
                RequiredStep("catalog", result.CatalogReason),
                result.ServiceOnly ? new("stock", "notApplicable", []) : RequiredStep("stock", result.StockReason ?? (result.CatalogReason == "ShopSetup.CatalogRequired" ? result.CatalogReason : null)),
                new("sample", preferences.SampleReceiptAcknowledgedAt is not null ? "complete" : "optional", []),
                new("bank", result.HasBankDetails ? "complete" : "optional", [])
            ],
            result.BlockingReasons,
            new(owner, owner || result.Role == ShopRole.Manager, owner),
            preferences);
    }

    public async Task<ErrorOr<ShopSetupPreferencesDto>> GetPreferencesAsync(GetShopSetupPreferencesQuery query, CancellationToken ct)
    {
        var member = await GetMemberAsync(query.ShopId, query.ActorUserId, query.ActiveShopId, ct);
        if (member is not { Role: ShopRole.Owner })
            return ShopSetupErrors.OwnerRequired;
        return ShopSetupPreferencesDto.From(await preferenceRepository.GetAsync(query.ShopId, ct));
    }

    public async Task<ErrorOr<ShopSetupSampleDto>> GetSampleAsync(GetShopSetupSampleQuery query, CancellationToken ct)
    {
        var evaluation = await EvaluateAsync(query.ShopId, query.ActorUserId, query.ActiveShopId, ct);
        if (evaluation.IsError)
            return evaluation.Errors;

        var result = evaluation.Value;
        if (result.CanTrade && result.Sample is { } sample)
            return sample;

        List<Error> errors =
        [
            Error.Conflict("ShopSetup.SampleUnavailable", "Complete the current shop setup requirements to preview a sample receipt."),
            .. result.BlockingReasons.Select(code => Error.Conflict(code, "Resolve this shop setup requirement."))
        ];
        return errors;
    }

    private async Task<ErrorOr<Evaluation>> EvaluateAsync(Guid shopId, Guid actor, Guid? activeShopId, CancellationToken ct)
    {
        if (await GetMemberAsync(shopId, actor, activeShopId, ct) is not { } membership)
            return ShopSetupErrors.Forbidden;

        var shop = membership.Shop;
        var facts = await readRepository.GetFactsAsync(shopId, ct);
        var seller = CaptureSellerProfile(shop);
        var candidates = await sampleBuilder.EvaluateAsync(shop, facts, seller.IsError ? null : seller.Value, actor, ct);
        var identityReason = facts.HasVerifiedOwner ? null : "ShopSetup.VerifiedOwnerRequired";
        var businessReason = HasBusinessDetails(shop) ? null : "ShopSetup.BusinessDetailsRequired";
        var taxReason = seller.IsError ? seller.FirstError.Code : null;
        var catalogReason = !facts.HasActiveGoods && !facts.HasActiveServices
            ? "ShopSetup.CatalogRequired"
            : candidates.Sample is null ? candidates.CatalogReason : null;
        var stockReason = facts.HasActiveGoods && !candidates.HasUsableStock ? candidates.StockReason : null;
        if (candidates.Sample is null && taxReason is null && catalogReason is null && stockReason is null)
            catalogReason = "ShopSetup.NoUsableCandidate";

        var blocking = new[] { identityReason, businessReason, taxReason, catalogReason, candidates.Sample is null ? stockReason : null }
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        return new Evaluation(membership.Role, facts.HasBankDetails, !facts.HasActiveGoods && facts.HasActiveServices,
            identityReason, businessReason, taxReason, catalogReason, stockReason, blocking, candidates.Sample);
    }

    private Task<ShopSetupMember?> GetMemberAsync(Guid shopId, Guid actor, Guid? activeShopId, CancellationToken ct) =>
        shopId == activeShopId ? readRepository.GetMemberAsync(shopId, actor, ct) : Task.FromResult<ShopSetupMember?>(null);

    private static ErrorOr<InvoiceSellerProfile> CaptureSellerProfile(Shop shop)
    {
        if (shop.TaxRegime is not (TaxRegime.Regular or TaxRegime.Composition or TaxRegime.Unregistered))
            return Error.Validation("Invoice.TaxProfileRequired", "Declare a supported tax profile.");
        var captured = InvoiceDocumentBuilder.CaptureSellerProfile(shop);
        if (!captured.IsError && !InvoiceNumberFormat.IsValidSeriesCode(captured.Value.SeriesCode))
            return Error.Validation("ShopSetup.InvoiceSeriesInvalid", "Choose a valid invoice series.");
        return captured;
    }

    private static bool HasBusinessDetails(Shop shop) =>
        new[] { shop.Name, shop.Address, shop.City, shop.State, shop.Pincode }.All(value => !string.IsNullOrWhiteSpace(value));

    private static ShopSetupStepDto RequiredStep(string id, string? reason) =>
        reason is null ? new(id, "complete", []) : new(id, "incomplete", [reason]);

    private sealed record Evaluation(ShopRole Role, bool HasBankDetails, bool ServiceOnly, string? IdentityReason,
        string? BusinessReason, string? TaxReason, string? CatalogReason, string? StockReason,
        IReadOnlyList<string> BlockingReasons, ShopSetupSampleDto? Sample)
    {
        public bool CanTrade => BlockingReasons.Count == 0 && Sample is not null;
    }
}

public sealed class GetShopSetupQueryHandler(ShopSetupService service)
{
    public Task<ErrorOr<ShopSetupStatusDto>> HandleAsync(GetShopSetupQuery query, CancellationToken ct) => service.GetAsync(query, ct);
}

public sealed class GetShopSetupPreferencesQueryHandler(ShopSetupService service)
{
    public Task<ErrorOr<ShopSetupPreferencesDto>> HandleAsync(GetShopSetupPreferencesQuery query, CancellationToken ct) => service.GetPreferencesAsync(query, ct);
}

public sealed class GetShopSetupSampleQueryHandler(ShopSetupService service)
{
    public Task<ErrorOr<ShopSetupSampleDto>> HandleAsync(GetShopSetupSampleQuery query, CancellationToken ct) => service.GetSampleAsync(query, ct);
}
