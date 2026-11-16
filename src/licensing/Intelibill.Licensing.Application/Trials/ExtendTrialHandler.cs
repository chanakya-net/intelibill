using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using static Intelibill.Licensing.Application.Trials.TrialExtensionFacts;

namespace Intelibill.Licensing.Application.Trials;

public sealed class ExtendTrialHandler(ITrialExtensionStore store, ISubscriptionAccessFactsReader reader,
    TimeProvider lifecycleClock, TimeProvider sessionClock)
{
    public Task<ErrorOr<TrialExtensionPreviewV1>> PreviewAsync(AdminSessionCaller caller, Guid account, Guid shop,
        TrialExtensionDraftV1? draft, CancellationToken ct) => store.InTransactionAsync<TrialExtensionPreviewV1>(async token =>
    {
        if (await AuthorizeAsync(caller, false, token) is { } denied) return denied;
        if (draft is null || Parse(draft.EndsAt, draft.Reason, draft.ReviewReference) is not { } facts)
            return TrialExtensionErrors.Invalid;

        await store.EnterAsync(caller.AdminId, account, token);
        await store.LockAccountAsync(account, token);
        if (await AuthorizeAsync(caller, false, token) is { } expired) return expired;
        var version = await store.ReadVersionAsync(account, shop, token);
        return await ReadPreviewAsync(account, shop, version, facts, token);
    }, ct);

    public Task<ErrorOr<TrialExtensionOutcome>> ExtendAsync(AdminSessionCaller caller, Guid account, Guid shop,
        ConfirmTrialExtensionV1? command, CancellationToken ct) => store.InTransactionAsync<TrialExtensionOutcome>(async token =>
    {
        if (await AuthorizeAsync(caller, true, token) is { } denied) return denied;
        if (command is not { Confirmed: true, CurrentVersion: >= 0, SourceHash: { Length: 64 } hash }
            || !hash.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
            || command.OperationKey == Guid.Empty || Parse(command.EndsAt, command.Reason, command.ReviewReference) is not { } facts)
            return TrialExtensionErrors.Invalid;

        var digest = Hash(TrialExtensionPolicy.Version, account, shop, command.OperationKey, command.CurrentVersion,
            hash, Format(facts.EndsAt), facts.Reason, facts.ReviewReference);
        await store.EnterAsync(caller.AdminId, account, token);
        await store.LockAccountAsync(account, token);
        if (await AuthorizeAsync(caller, true, token) is { } expired) return expired;
        if (await store.ReadAsync(account, command.OperationKey, token) is { } previous)
            return previous.AdminId == caller.AdminId && previous.PayloadDigest == digest
                ? new TrialExtensionOutcome(previous.Result, Created: false) : TrialExtensionErrors.PayloadMismatch;

        var version = await store.ReadVersionAsync(account, shop, token);
        if (version != command.CurrentVersion) return TrialExtensionErrors.VersionConflict;
        var preview = await ReadPreviewAsync(account, shop, version, facts, token);
        if (preview.IsError) return preview.Errors;
        if (preview.Value.SourceHash != hash) return TrialExtensionErrors.VersionConflict;

        var result = Result(preview.Value, command.OperationKey);
        await store.SaveAsync(new(result, caller.AdminId, caller.SessionId, digest),
            ParseEnd(result.PreviousEndsAt), facts.EndsAt, token);
        if (await AuthorizeAsync(caller, true, token) is { } lost) return lost;
        return new TrialExtensionOutcome(result, Created: true);
    }, ct);

    public Task<ErrorOr<TrialExtensionResultV1>> RecoverAsync(AdminSessionCaller caller, Guid account, Guid shop,
        Guid operationKey, CancellationToken ct) => store.InTransactionAsync<TrialExtensionResultV1>(async token =>
    {
        if (await AuthorizeAsync(caller, true, token) is { } denied) return denied;
        await store.EnterAsync(caller.AdminId, account, token);
        var receipt = await store.ReadAsync(account, operationKey, token);
        return receipt is not null && receipt.AdminId == caller.AdminId && receipt.Result.ShopId == shop
            ? receipt.Result : TrialExtensionErrors.OperationNotFound;
    }, ct);

    private async Task<ErrorOr<TrialExtensionPreviewV1>> ReadPreviewAsync(Guid account, Guid shop, long version,
        TrialExtensionFacts facts, CancellationToken ct)
    {
        var affiliation = await store.ReadShopAsync(account, shop, ct);
        if (affiliation is null) return TrialExtensionErrors.TrialNotFound;
        var coverage = await reader.ReadShopAsync(shop, ct);
        if (coverage is null || coverage.AccountId != account || coverage.Term is not { Trial: { } trial } term)
            return TrialExtensionErrors.TrialNotFound;
        if (term.AccountId != account || SubscriptionStatusProjection.ToFacts(term).Evidence != TermEvidenceState.Complete)
            return TrialExtensionErrors.NotEligible;

        var at = SubscriptionStatusProjection.EvaluationInstant(lifecycleClock);
        var currentEnd = trial.ExtendedEndsAt ?? term.EndsAt;
        if (!TrialExtensionPolicy.ValidEnd(facts.EndsAt, currentEnd, at)) return TrialExtensionErrors.EndNotLater;
        var window = new TrialInterval(currentEnd > at ? currentEnd : at, facts.EndsAt);
        var overlaps = await store.ReadOverlapsAsync(account, shop, window, ct);
        var capReached = !TrialEligibilityPolicy.HasCapacity(
            overlaps.Select(row => new TrialInterval(row.StartsAt, row.EndsAt)).ToArray(), window, trial.AccountTrialCap);
        var hash = Hash(TrialExtensionPolicy.Version, account, shop, term.TermId, affiliation.AffiliationRequestId,
            version, Format(currentEnd), Format(facts.EndsAt), facts.Reason, facts.ReviewReference, overlaps, capReached);
        return new TrialExtensionPreviewV1(account, shop, affiliation.ShopName, term.TermId, Format(term.StartsAt),
            Format(term.EndsAt), Format(currentEnd), Format(facts.EndsAt), facts.Reason, facts.ReviewReference,
            version, hash, at, trial.AccountTrialCap, capReached);
    }

    private async Task<Error?> AuthorizeAsync(AdminSessionCaller caller, bool recentMfa, CancellationToken ct)
    {
        var (actor, session) = await store.LockProviderAsync(caller, ct);
        var now = sessionClock.GetUtcNow();
        if (session is null || now >= caller.AccessExpiresAt || !session.IsLiveAt(now)
            || actor is not { DeactivatedAt: null, MfaEnrolledAt: not null })
            return AdminRefreshErrors.SessionEnded;
        if (!actor.Capabilities.HasFlag(AdminCapability.LicensingManager)) return TrialExtensionErrors.AccessDenied;
        if (recentMfa && !new ProviderActor(caller.AdminId, session.MfaVerifiedAt).HasRecentMfa(now))
            return TrialExtensionErrors.RecentMfa;
        return null;
    }

    private static DateTimeOffset ParseEnd(string value) => Grants.ComplimentaryGrantFacts.ParseInstant(value)!.Value;

    private static TrialExtensionResultV1 Result(TrialExtensionPreviewV1 preview, Guid key) =>
        new(preview.AccountId, preview.ShopId, preview.ShopName, preview.TermId, preview.StartsAt, preview.OriginalEndsAt,
            preview.CurrentEndsAt, preview.EndsAt, preview.Reason, preview.ReviewReference, preview.CurrentVersion,
            preview.CurrentVersion + 1, preview.SourceHash, preview.EvaluatedAt, preview.AccountTrialCap,
            preview.AccountTrialCapReached, key, Guid.NewGuid());
}
