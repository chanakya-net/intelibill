using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Status;

/// <summary>
/// One persisted coverage term with its optional frozen supplement. Supplement fields are all null for a
/// coverage-only row; the evaluation reports that as incomplete evidence rather than guessing. A paid renewal chain
/// is read as its latest period with <c>PaidFrom</c>, the start of the contiguous same-anchor periods before it;
/// <c>RenewedAtEnd</c> marks a period already extended by a paid successor.
/// </summary>
public sealed record TermFactsRow(Guid TermId, Guid AccountId, string Mode, int Capacity, DateTimeOffset StartsAt,
    DateTimeOffset EndsAt, string? Basis, string? BillingPeriod, DateTime? AnchorLocal, string? TimeZone,
    int? PeriodOrdinal, int? MemberAllowance, long? PricePaise, string? Currency, Guid? PlanVersionId,
    string? SourceReference, bool? DevelopmentFixture, string? CalendarVersion, string? AccessPolicyVersion,
    TrialTermProvenance? Trial = null, ComplimentaryTermProvenance? Complimentary = null,
    ScheduledSubscriptionChange? Cancellation = null, DateTimeOffset? PaidFrom = null, bool RenewedAtEnd = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    RefundTermBoundary? RefundBoundary = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? SlotAssignmentId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? HandoffEndsAt = null);

/// <summary>The resulting instants of the term's latest effective refund access decision; operational columns only.</summary>
public sealed record RefundTermBoundary(DateTimeOffset PaidThrough, DateTimeOffset AccessEndsAt);

public sealed record TrialTermProvenance(string ComputationVersion, int MemberCap, int ActiveMembers,
    int AccountTrialCap, bool DevelopmentFixture, bool ConflictingCoverage = false, DateTimeOffset? ExtendedEndsAt = null);

/// <summary>
/// Operational provenance of a dated complimentary grant: never its reason, actor or receipt. <c>Consistent</c> is
/// false when the grant, its coverage term and the shop's link disagree on dates or account.
/// </summary>
public sealed record ComplimentaryTermProvenance(string PolicyVersion, int MemberAllowance, int ShopCount, bool Consistent);

/// <summary>
/// The shop's applied affiliation (its account), the baseline term it was assigned (affiliation or trial), and
/// the complimentary grants naming it in that same account, ordered by term id. Validated paid predecessors
/// are retained newest first so a future term's rules do not apply before its start.
/// </summary>
public sealed record ShopCoverage(Guid AccountId, TermFactsRow? Term, IReadOnlyList<TermFactsRow>? Grants = null,
    IReadOnlyList<TermFactsRow>? PaidPredecessors = null, DateTimeOffset? ConvertedPaidStartsAt = null);

public sealed record AccountTermRow(TermFactsRow Term, CoveredShopV1[] CoveredShops, int CoveredShopCount);

/// <summary>
/// Reads immutable evaluation input under the row-level scope already set for the transaction. Future trial,
/// grant, restriction and payment sources supply their own facts through this seam; nothing here is computed status.
/// </summary>
public interface ISubscriptionAccessFactsReader
{
    Task<ShopCoverage?> ReadShopAsync(Guid shop, CancellationToken ct, DateTimeOffset? evaluatedAt = null);
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
