using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Status;

/// <summary>
/// One persisted coverage term with its optional frozen supplement. Supplement fields are all null for a
/// coverage-only row; the evaluation reports that as incomplete evidence rather than guessing.
/// </summary>
public sealed record TermFactsRow(Guid TermId, Guid AccountId, string Mode, int Capacity, DateTimeOffset StartsAt,
    DateTimeOffset EndsAt, string? Basis, string? BillingPeriod, DateTime? AnchorLocal, string? TimeZone,
    int? PeriodOrdinal, int? MemberAllowance, long? PricePaise, string? Currency, Guid? PlanVersionId,
    string? SourceReference, bool? DevelopmentFixture, string? CalendarVersion, string? AccessPolicyVersion);

/// <summary>The shop's applied affiliation (its account) and the term it was assigned, when it has one.</summary>
public sealed record ShopCoverage(Guid AccountId, TermFactsRow? Term);

public sealed record AccountTermRow(TermFactsRow Term, CoveredShopV1[] CoveredShops, int CoveredShopCount);

/// <summary>
/// Reads immutable evaluation input under the row-level scope already set for the transaction. Future trial,
/// grant, restriction and payment sources supply their own facts through this seam; nothing here is computed status.
/// </summary>
public interface ISubscriptionAccessFactsReader
{
    Task<ShopCoverage?> ReadShopAsync(Guid shop, CancellationToken ct);
    Task<IReadOnlyList<AccountTermRow>> ReadAccountAsync(Guid account, Guid? after, int take, CancellationToken ct);
}

/// <summary>Transaction-local scope and live authority checks for status reads; no broad unscoped store.</summary>
public interface ISubscriptionStatusStore
{
    /// <summary>Narrow row-level security to one shop's own coverage for the rest of the transaction.</summary>
    Task UseShopAsync(Guid shop, CancellationToken ct);

    /// <summary>Provider reads only: narrow row-level security to one account for the rest of the transaction.</summary>
    Task UseAccountAsync(Guid account, CancellationToken ct);

    /// <summary>The actor's live Owner/Manager billing role in the account, locked FOR SHARE until commit.</summary>
    Task<bool> HoldsCommercialRoleAsync(Guid account, Guid actor, CancellationToken ct);

    Task<ErrorOr<T>> InProviderTransactionAsync<T>(Func<CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken ct);

    Task<(AdminIdentity? Actor, AdminSession? Session)> LockProviderAsync(AdminSessionCaller caller, CancellationToken ct);
}

public static class SubscriptionStatusErrors
{
    public static Error ShopNotCovered => Error.NotFound("Status.ShopNotFound", "No status exists for that shop in this account.");
    public static Error ProviderDenied => Error.Forbidden("Status.AccessDenied",
        "Only Support, Billing or Licensing Manager administrators can read access status.");
    public static Error Unavailable => Error.Failure("Status.Unavailable", "Access status is temporarily unavailable.");
}
