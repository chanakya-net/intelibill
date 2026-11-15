using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.Catalogue;

/// <summary>A full replacement of one plan draft. A null fact is saved as missing.</summary>
public sealed record PlanDraftRequest(
    int? ExpectedVersion,
    string? Kind,
    string? Name,
    long? MonthlyPricePaise,
    long? AnnualPricePaise,
    int? IncludedShops,
    int? MembersPerShop,
    bool? ExtraShopsOffered,
    long? ExtraShopMonthlyPricePaise,
    long? ExtraShopAnnualPricePaise,
    string? CustomerTermsVersion,
    string? RefundPolicyVersion,
    string? Reason);

public sealed record TrialPolicyDraftRequest(
    int? ExpectedVersion,
    int? ActiveMemberCap,
    int? MaxConcurrentTrialShops,
    string? TrialTermsVersion);

public sealed record PlanDraftView(
    Guid VersionId,
    int VersionNumber,
    string Status,
    int Version,
    string Name,
    string Currency,
    long? MonthlyPricePaise,
    long? AnnualPricePaise,
    int? IncludedShops,
    int? MembersPerShop,
    bool? ExtraShopsOffered,
    long? ExtraShopMonthlyPricePaise,
    long? ExtraShopAnnualPricePaise,
    string? CustomerTermsVersion,
    string? RefundPolicyVersion,
    string? NegotiationReason,
    string[] Missing,
    bool Complete,
    DateTimeOffset UpdatedAt);

public sealed record PlanItem(Guid PlanId, string Kind, PlanDraftView Draft);
public sealed record PlanPage(IReadOnlyList<PlanItem> Items, Guid? NextCursor);
public sealed record PlanDraftSaved(PlanItem Item, bool Created);

public sealed record TrialDraftView(
    Guid VersionId,
    int VersionNumber,
    string Status,
    int Version,
    int DurationDays,
    int? ActiveMemberCap,
    int? MaxConcurrentTrialShops,
    string? TrialTermsVersion,
    string[] Missing,
    bool Complete,
    DateTimeOffset UpdatedAt);

public sealed record TrialDraftEnvelope(TrialDraftView? Draft);
public sealed record TrialDraftSaved(TrialDraftEnvelope Body, bool Created);

public interface ICatalogueDraftStore
{
    /// <summary>One READ COMMITTED transaction, committed only when the operation succeeds.</summary>
    Task<ErrorOr<T>> InTransactionAsync<T>(Func<CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken token);

    /// <summary>Lock order: the identity (shared), then its session; finally binds the catalogue context.</summary>
    Task<(AdminIdentity? Actor, AdminSession? Session)> LockAuthorityAsync(AdminSessionCaller caller, CancellationToken token);

    Task EnterCatalogueAsync(Guid adminId, CancellationToken token);
    Task<IReadOnlyList<(Plan Plan, PlanVersion Draft)>> ListPlanDraftsAsync(Guid? after, int take, CancellationToken token);
    Task<(Plan? Plan, PlanVersion? Draft)> LockPlanAsync(Guid planId, CancellationToken token);
    Task<TrialPolicyVersion?> LockTrialDraftAsync(CancellationToken token);
    Task<int> NextTrialVersionNumberAsync(CancellationToken token);
    void Add(object entity);
}

public static class CatalogueErrors
{
    public static Error AccessDenied => Error.Forbidden("Catalogue.AccessDenied", "Only a Licensing Manager can draft plans.");
    public static Error DraftInvalid => Error.Validation("Catalogue.DraftInvalid", "The draft could not be read. Check every field.");
    public static Error NameInvalid => Error.Validation("Catalogue.NameInvalid", "Enter a plan name of 1 to 80 characters.");
    public static Error AmountInvalid => Error.Validation("Catalogue.AmountInvalid", "Prices are whole paise from 0 to 1,000,000,000,000.");
    public static Error CapacityInvalid => Error.Validation("Catalogue.CapacityInvalid", "Capacities are whole numbers from 1 to 100,000; unlimited is not allowed.");
    public static Error ExtraShopTermsInvalid => Error.Validation("Catalogue.ExtraShopTermsInvalid", "Extra-shop prices need an extra-shop offer on a multi-shop plan.");
    public static Error PolicyVersionInvalid => Error.Validation("Catalogue.PolicyVersionInvalid", "Policy versions use letters, digits, dot, dash or underscore.");
    public static Error CustomReasonRequired => Error.Validation("Catalogue.CustomReasonRequired", "Negotiated Custom terms need a reason of 1 to 500 characters.");
    public static Error DraftNotFound => Error.NotFound("Catalogue.DraftNotFound", "The draft was not found. Reload the catalogue.");
    public static Error VersionConflict => Error.Conflict("Catalogue.DraftVersionConflict", "The draft changed. Reload before editing.");
    public static Error KindFixed => Error.Conflict("Catalogue.PlanKindFixed", "A plan's kind cannot change.");
    public static Error Busy => Error.Failure("Catalogue.Busy", "The catalogue is busy. Try again.");
}
