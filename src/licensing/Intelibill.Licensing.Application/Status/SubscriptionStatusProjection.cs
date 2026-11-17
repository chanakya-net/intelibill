using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Status;

/// <summary>
/// The single evaluation and projection both merchant and provider responses share. Commercial facts are
/// projected only when the caller's authority was confirmed; nothing is cached or written back.
/// </summary>
public static class SubscriptionStatusProjection
{
    public static SubscriptionAccessFacts ToFacts(TermFactsRow row)
    {
        if (row.Cancellation is { } change && (row.Basis != nameof(SubscriptionBasis.Paid)
            || change.EffectiveAt != row.EndsAt || change.PolicyVersion != SubscriptionCancellationsV1.Policy
            || change.ChangeId == Guid.Empty || !ValidRevision(change) || change.ShopCount <= 0 || change.ShopCount > row.Capacity
            || !SubscriptionCancellationsV1.IsDigest(change.ShopHash)
            || row.Trial is not null || row.Complimentary is not null))
            return new SubscriptionAccessFacts(row.StartsAt, row.EndsAt, TermEvidenceState.NeedsReview);
        if (row.Complimentary is { } grant)
            return new SubscriptionAccessFacts(row.StartsAt, row.EndsAt,
                ComplimentaryComplete(row, grant) ? TermEvidenceState.Complete : TermEvidenceState.NeedsReview,
                SubscriptionBasis.Complimentary);
        if (row.Trial is { } trial)
        {
            var complete = row.Basis is null && row.Mode == "Independent" && row.Capacity == 1
                && trial is { ComputationVersion: TrialHistory.ComputationVersion, MemberCap: > 0, ActiveMembers: > 0,
                    AccountTrialCap: > 0, ConflictingCoverage: false }
                && trial.ActiveMembers <= trial.MemberCap && row.EndsAt - row.StartsAt == TimeSpan.FromDays(ShopTrialsV1.DurationDays)
                && (trial.ExtendedEndsAt is null || trial.ExtendedEndsAt > row.EndsAt);
            return new SubscriptionAccessFacts(row.StartsAt, trial.ExtendedEndsAt ?? row.EndsAt,
                complete ? TermEvidenceState.Complete : TermEvidenceState.NeedsReview, SubscriptionBasis.Trial);
        }

        if (row.Basis is null)
            return new SubscriptionAccessFacts(row.StartsAt, row.EndsAt, TermEvidenceState.Incomplete);
        if (row.CalendarVersion != BillingCalendar.Version || row.AccessPolicyVersion != GracePolicy.Version
            || !Enum.TryParse<SubscriptionBasis>(row.Basis, out var basis)
            || !Enum.TryParse<BillingPeriod>(row.BillingPeriod, out var period)
            || row.AnchorLocal is not { } local || row.TimeZone is not { } zone || row.PeriodOrdinal is not { } ordinal)
            return new SubscriptionAccessFacts(row.StartsAt, row.EndsAt, TermEvidenceState.NeedsReview);

        var anchor = new BillingAnchor(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone, period);
        // A renewed chain is continuous paid access from its first period to its latest end; grace follows only that end.
        var starts = basis == SubscriptionBasis.Paid && row.PaidFrom is { } from && from < row.StartsAt ? from : row.StartsAt;
        return new SubscriptionAccessFacts(starts, row.EndsAt, Agrees(row, basis, anchor, ordinal)
            ? TermEvidenceState.Complete : TermEvidenceState.NeedsReview, basis, anchor, ordinal,
            row.Cancellation is { } saved ? new ScheduledCancellation(saved.EffectiveAt, saved.WithdrawnAt) : null,
            RenewedAtEnd: row.RenewedAtEnd);
    }

    // A scheduled cancellation is an odd revision; a withdrawal is the next even one, recorded before the boundary.
    private static bool ValidRevision(ScheduledSubscriptionChange change) => change.WithdrawnAt is { } withdrawn
        ? SubscriptionCancellationsV1.IsOpenVersion(change.Version) && change.Version > 0 && withdrawn < change.EffectiveAt
        : SubscriptionCancellationsV1.IsScheduledVersion(change.Version);

    /// <summary>The term's cancellation that is still in force, if any; a withdrawn one is history only.</summary>
    public static ScheduledSubscriptionChange? ActiveCancellation(TermFactsRow row) =>
        row.Cancellation is { WithdrawnAt: null } saved ? saved : null;

    // A grant's frozen capacity, mode and dates must agree with its coverage term; nothing else is inferred.
    private static bool ComplimentaryComplete(TermFactsRow row, ComplimentaryTermProvenance grant) =>
        row.Basis is null && row.Trial is null && grant.Consistent && grant.PolicyVersion == ComplimentaryAccessPolicy.Version
        && ComplimentaryAccessPolicy.ValidCapacity(grant.ShopCount, grant.MemberAllowance)
        && row.Capacity == grant.ShopCount && row.Mode == ComplimentaryAccessPolicy.CoverageMode(grant.ShopCount);

    // A paid term is exactly one calendar period of its retained anchor; anything else needs review.
    private static bool Agrees(TermFactsRow row, SubscriptionBasis basis, BillingAnchor anchor, int ordinal)
    {
        if (ordinal is < 0 or > BillingCalendar.MaxPeriodOrdinal || !BillingCalendar.IsSupported(anchor)) return false;
        if (basis != SubscriptionBasis.Paid) return true;
        return BillingCalendar.TryBoundary(anchor, ordinal) == row.StartsAt
            && BillingCalendar.TryBoundary(anchor, ordinal + 1L) == row.EndsAt
            && BillingCalendar.TryBoundary(anchor, ordinal + 2L) is not null;
    }

    public static SubscriptionTermStatusV1 Project(TermFactsRow row, DateTimeOffset at, bool commercial)
    {
        var facts = ToFacts(row);
        return Project(row, facts, SubscriptionAccessPolicy.Evaluate(facts, at), commercial);
    }

    private static SubscriptionTermStatusV1 Project(TermFactsRow row, SubscriptionAccessFacts facts,
        SubscriptionAccessEvaluation result, bool commercial)
    {
        var complete = facts.Evidence == TermEvidenceState.Complete;
        var basis = row.Trial is not null ? nameof(SubscriptionBasis.Trial)
            : row.Complimentary is not null ? nameof(SubscriptionBasis.Complimentary) : row.Basis;
        return new SubscriptionTermStatusV1(row.TermId, result.State.ToString(), facts.Evidence.ToString(),
            complete ? basis : null, complete ? row.BillingPeriod : null, BillingCalendar.Zone,
            result.StartsAt, result.PaidThrough, result.GraceEndsAt, result.AccessEndsAt, result.NextDeadline,
            result.NextPeriodEndsAt, row.Mode, row.Capacity,
            complete ? row.Trial?.MemberCap ?? row.Complimentary?.MemberAllowance ?? row.MemberAllowance : null,
            row.Trial?.ComputationVersion ?? row.Complimentary?.PolicyVersion ?? row.SourceReference,
            row.Trial?.DevelopmentFixture ?? row.DevelopmentFixture == true,
            commercial && row.Basis is not null && row.Trial is null && row.Complimentary is null
                ? new SubscriptionCommercialV1(row.PricePaise, row.Currency, row.PlanVersionId) : null,
            complete && ActiveCancellation(row) is { } saved
                ? new(saved.ChangeId, saved.Version, saved.EffectiveAt, saved.PolicyVersion) : null);
    }

    /// <summary>
    /// The shop's one shown source at <paramref name="at"/>: its baseline (affiliation or trial) term unless that
    /// supplies no current access and a complimentary grant naming the shop does (see <see cref="ComplimentaryAccessPolicy"/>).
    /// Account suspension intervals, when a source supplies them, are applied to the chosen term.
    /// </summary>
    public static SubscriptionTermStatusV1? Choose(ShopCoverage? coverage, DateTimeOffset at, bool commercial,
        IReadOnlyList<SuspensionInterval>? suspensions = null)
    {
        if (coverage is null) return null;
        var grants = coverage.Grants ?? [];
        var baseline = coverage.Term is { } term ? ToFacts(term) : null;
        var grantFacts = grants.Select(ToFacts).ToArray();
        var chosen = ComplimentaryAccessPolicy.Select(baseline, grantFacts, at);
        if (chosen is not { } index) return null;
        var (row, facts) = index < 0 ? (coverage.Term!, baseline!) : (grants[index], grantFacts[index]);
        return Project(row, facts, ComplimentaryAccessPolicy.Evaluate(facts, at, suspensions), commercial);
    }

    public static ShopSubscriptionStatusV1 Shop(Guid shop, DateTimeOffset at, ShopCoverage? coverage, bool commercial,
        EntitlementSynchronizationV1? sync = null)
    {
        var term = Choose(coverage, at, commercial);
        return new ShopSubscriptionStatusV1(SubscriptionStatusV1.SchemaVersion, shop, at,
            term?.State ?? "PendingActivation", term, commercial ? coverage?.AccountId : null, ObserveOnly: true,
            sync?.State ?? SubscriptionStatusV1.NotAvailable, SubscriptionStatusV1.NotAvailable, sync);
    }

    public static AccountSubscriptionStatusV1 Account(Guid account, DateTimeOffset at, IReadOnlyList<AccountTermRow> rows,
        bool commercial, IReadOnlyDictionary<Guid, EntitlementSynchronizationV1> sync)
    {
        var page = rows.Take(SubscriptionStatusV1.PageSize).ToArray();
        var items = page.Select(row => new AccountTermStatusV1(Project(row.Term, at, commercial),
            [.. row.CoveredShops.Select(shop => shop with { Synchronization = sync.GetValueOrDefault(shop.ShopId) })],
            row.CoveredShopCount)).ToArray();
        return new AccountSubscriptionStatusV1(SubscriptionStatusV1.SchemaVersion, account, at, items,
            rows.Count > page.Length ? page[^1].Term.TermId : null, ObserveOnly: true,
            SubscriptionStatusV1.Summarize(items.SelectMany(item => item.CoveredShops)), SubscriptionStatusV1.NotAvailable);
    }

    /// <summary>PostgreSQL keeps microseconds; one evaluation instant per response, truncated to match storage.</summary>
    public static DateTimeOffset EvaluationInstant(TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
    }
}
