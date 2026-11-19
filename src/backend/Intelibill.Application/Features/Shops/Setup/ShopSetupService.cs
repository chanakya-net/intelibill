using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Security;
using ErrorOr;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Application.Features.Shops.DTOs;
using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Shops.Setup;

public sealed class ShopSetupService(
    IShopSetupReadRepository readRepository,
    IShopSetupPreferencesRepository preferenceRepository,
    ShopSetupSampleBuilder sampleBuilder,
    ShopSetupAccess access)
{
    public Task<ErrorOr<ShopSetupStatusDto>> GetAsync(GetShopSetupQuery query, CancellationToken ct) =>
        ReadActiveAsync<StatusFacts, ShopSetupStatusDto>(query.ShopId, query.ActorUserId, query.ActiveShopId, query.Session,
            async (shop, role, token) => new StatusFacts(await EvaluateAsync(shop, query.ActorUserId, role, token),
                await preferenceRepository.GetAsync(query.ShopId, token)),
            (facts, hint) => ToStatus(query.ShopId, facts, hint), ct);

    public Task<ErrorOr<ShopSetupPreferencesDto>> GetPreferencesAsync(GetShopSetupPreferencesQuery query, CancellationToken ct) =>
        ReadActiveAsync<PreferenceFacts, ShopSetupPreferencesDto>(query.ShopId, query.ActorUserId, query.ActiveShopId, query.Session,
            async (_, role, token) => role == ShopRole.Owner
                ? new PreferenceFacts(await preferenceRepository.GetAsync(query.ShopId, token))
                : ShopSetupErrors.OwnerRequired,
            (facts, hint) => ShopSetupPreferencesDto.From(facts.Saved, hint.CanSavePreferences), ct);

    public Task<ErrorOr<ShopSetupSampleDto>> GetSampleAsync(GetShopSetupSampleQuery query, CancellationToken ct) =>
        ReadActiveAsync<Evaluation, ShopSetupSampleDto>(query.ShopId, query.ActorUserId, query.ActiveShopId, query.Session,
            async (shop, role, token) => await EvaluateAsync(shop, query.ActorUserId, role, token),
            (result, _) => ToSample(result), ct);

    private static ErrorOr<ShopSetupStatusDto> ToStatus(Guid shopId, StatusFacts facts, ShopSetupAccessDto access)
    {
        var result = facts.Evaluation;
        var preferences = ShopSetupPreferencesDto.From(facts.Preferences, access.CanSavePreferences);
        return new ShopSetupStatusDto(shopId, 1, result.CanTrade,
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
            new(access.WritableProfileFields.Count > 0,
                access.HasOperatingAccess && result.Role is ShopRole.Owner or ShopRole.Manager, access.CanSavePreferences),
            preferences) { SetupAccess = access };
    }

    private static ErrorOr<ShopSetupSampleDto> ToSample(Evaluation result)
    {
        if (result.CanTrade && result.Sample is { } sample)
            return sample;

        List<Error> errors =
        [
            Error.Conflict("ShopSetup.SampleUnavailable", "Complete the current shop setup requirements to preview a sample receipt."),
            .. result.BlockingReasons.Select(code => Error.Conflict(code, "Resolve this shop setup requirement."))
        ];
        return errors;
    }

    /// <summary>
    /// Setup status, sample and preferences describe the shop the calling device is in. The facts load
    /// first; the access they are reported with is supplied afterwards, from the decision that holds then.
    /// </summary>
    private async Task<ErrorOr<T>> ReadActiveAsync<TData, T>(Guid shopId, Guid actor, Guid? activeShopId, SessionClaims? session,
        Func<Shop, ShopRole, CancellationToken, Task<ErrorOr<TData>>> load,
        Func<TData, ShopSetupAccessDto, ErrorOr<T>> project, CancellationToken ct)
    {
        if (shopId != activeShopId)
            return ShopSetupErrors.Forbidden;

        var result = await access.ReadAsync(session, actor, shopId, async (role, token) =>
        {
            if (await readRepository.GetMemberAsync(shopId, actor, token) is not { } membership)
                return ShopSetupErrors.Forbidden;
            return await load(membership.Shop, role, token);
        }, project, ct, activeShopOnly: true);
        return result.IsError && result.FirstError.Code == "Shop.MembershipNotFound" ? ShopSetupErrors.Forbidden : result;
    }

    private async Task<Evaluation> EvaluateAsync(Shop shop, Guid actor, ShopRole role, CancellationToken ct)
    {
        var facts = await readRepository.GetFactsAsync(shop.Id, ct);
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
        return new Evaluation(role, facts.HasBankDetails, !facts.HasActiveGoods && facts.HasActiveServices,
            identityReason, businessReason, taxReason, catalogReason, stockReason, blocking, candidates.Sample);
    }

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

    private sealed record StatusFacts(Evaluation Evaluation, ShopSetupPreferences? Preferences);

    private sealed record PreferenceFacts(ShopSetupPreferences? Saved);

    private sealed record Evaluation(ShopRole Role, bool HasBankDetails, bool ServiceOnly, string? IdentityReason,
        string? BusinessReason, string? TaxReason, string? CatalogReason, string? StockReason,
        IReadOnlyList<string> BlockingReasons, ShopSetupSampleDto? Sample)
    {
        public bool CanTrade => BlockingReasons.Count == 0 && Sample is not null;
    }
}

public sealed class GetShopSetupQueryHandler(ShopSetupService service)
{
    [LicenceAction(LicenceAction.History)]
    public Task<ErrorOr<ShopSetupStatusDto>> HandleAsync(GetShopSetupQuery query, CancellationToken ct) => service.GetAsync(query, ct);
}

public sealed class GetShopSetupPreferencesQueryHandler(ShopSetupService service)
{
    [LicenceAction(LicenceAction.History)]
    public Task<ErrorOr<ShopSetupPreferencesDto>> HandleAsync(GetShopSetupPreferencesQuery query, CancellationToken ct) => service.GetPreferencesAsync(query, ct);
}

public sealed class GetShopSetupSampleQueryHandler(ShopSetupService service)
{
    [LicenceAction(LicenceAction.History)]
    public Task<ErrorOr<ShopSetupSampleDto>> HandleAsync(GetShopSetupSampleQuery query, CancellationToken ct) => service.GetSampleAsync(query, ct);
}
