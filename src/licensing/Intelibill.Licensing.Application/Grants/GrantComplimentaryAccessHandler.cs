using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.Grants;

/// <summary>
/// Provider preview, confirmation and same-operation recovery for dated complimentary access. Only a live,
/// MFA-enrolled administrator holding the persisted Licensing Manager grant on a live session qualifies; writes
/// and recovery also need recent MFA. Authority is re-read under lock before any receipt is disclosed, after the
/// account fence wait and again before commit. A committed replay precedes every current-time or version check.
/// </summary>
public sealed class GrantComplimentaryAccessHandler(IComplimentaryGrantStore store, ISubscriptionAccessFactsReader reader,
    TimeProvider lifecycleClock, TimeProvider sessionClock)
{
    public Task<ErrorOr<ComplimentaryGrantPreviewV1>> PreviewAsync(AdminSessionCaller caller, Guid account,
        ComplimentaryGrantDraftV1? draft, CancellationToken ct) => store.InTransactionAsync<ComplimentaryGrantPreviewV1>(async token =>
    {
        if (await AuthorizeAsync(caller, recentMfa: false, token) is { } denied) return denied;
        var facts = draft is null ? null
            : ComplimentaryGrantFacts.From(draft.ShopIds, draft.StartsAt, draft.EndsAt, draft.MemberAllowance, draft.Reason);
        if (account == Guid.Empty || facts is null) return ComplimentaryGrantErrors.Invalid;

        await store.EnterAsync(caller.AdminId, account, token);
        var source = await ComplimentaryGrantSource.ReadAsync(store, reader, account, facts, Instant(), token);
        return source.IsError ? source.Errors : source.Value.Preview(account, facts);
    }, ct);

    public Task<ErrorOr<ComplimentaryGrantOutcome>> GrantAsync(AdminSessionCaller caller, Guid account,
        ConfirmComplimentaryGrantV1? request, CancellationToken ct) => store.InTransactionAsync<ComplimentaryGrantOutcome>(async token =>
    {
        if (await AuthorizeAsync(caller, recentMfa: true, token) is { } denied) return denied;
        var facts = Validate(account, request);
        if (facts is null) return ComplimentaryGrantErrors.Invalid;
        var digest = ComplimentaryGrantSource.Digest(account, request!.OperationKey, facts, request.ExpectedVersion, request.SourceHash!);

        await store.EnterAsync(caller.AdminId, account, token);
        await store.LockAccountAsync(account, token);
        if (await AuthorizeAsync(caller, recentMfa: true, token) is { } expired) return expired;
        if (await store.ReadAsync(account, request.OperationKey, token) is { } previous)
            return previous.AdminId == caller.AdminId && previous.PayloadDigest == digest
                ? new ComplimentaryGrantOutcome(ComplimentaryGrantSource.Result(previous), Created: false)
                : ComplimentaryGrantErrors.PayloadMismatch;

        // A preview made before any later grant is stale, whatever else changed since.
        if (await store.ReadRevisionAsync(account, token) != request.ExpectedVersion) return ComplimentaryGrantErrors.VersionConflict;
        var source = await ComplimentaryGrantSource.ReadAsync(store, reader, account, facts, Instant(), token);
        if (source.IsError) return source.Errors;
        if (source.Value.Revision != request.ExpectedVersion || source.Value.Hash != request.SourceHash)
            return ComplimentaryGrantErrors.VersionConflict;

        var grant = new StoredComplimentaryGrant(Guid.NewGuid(), Guid.NewGuid(), account, request.OperationKey, caller.AdminId,
            caller.SessionId, facts, request.ExpectedVersion, request.SourceHash!, digest, source.Value.At, source.Value.Shops);
        await store.SaveAsync(grant, token);
        if (await AuthorizeAsync(caller, recentMfa: true, token) is { } lost) return lost;
        return new ComplimentaryGrantOutcome(ComplimentaryGrantSource.Result(grant), Created: true);
    }, ct);

    public Task<ErrorOr<ComplimentaryGrantResultV1>> RecoverAsync(AdminSessionCaller caller, Guid account, Guid operationKey,
        CancellationToken ct) => store.InTransactionAsync<ComplimentaryGrantResultV1>(async token =>
    {
        if (await AuthorizeAsync(caller, recentMfa: true, token) is { } denied) return denied;
        if (account == Guid.Empty || operationKey == Guid.Empty) return ComplimentaryGrantErrors.Missing;
        await store.EnterAsync(caller.AdminId, account, token);
        var previous = await store.ReadAsync(account, operationKey, token);
        return previous is not null && previous.AdminId == caller.AdminId
            ? ComplimentaryGrantSource.Result(previous) : ComplimentaryGrantErrors.Missing;
    }, ct);

    private static ComplimentaryGrantFacts? Validate(Guid account, ConfirmComplimentaryGrantV1? request) =>
        request is { Confirmed: true, ExpectedVersion: >= 0 } && account != Guid.Empty && request.OperationKey != Guid.Empty
        && ComplimentaryGrantSource.ValidHash(request.SourceHash)
            ? ComplimentaryGrantFacts.From(request.ShopIds, request.StartsAt, request.EndsAt, request.MemberAllowance, request.Reason)
            : null;

    private DateTimeOffset Instant() => SubscriptionStatusProjection.EvaluationInstant(lifecycleClock);

    private async Task<Error?> AuthorizeAsync(AdminSessionCaller caller, bool recentMfa, CancellationToken ct)
    {
        var (actor, session) = await store.LockProviderAsync(caller, ct);
        var now = sessionClock.GetUtcNow();
        if (session is null || now >= caller.AccessExpiresAt || !session.IsLiveAt(now)
            || actor is not { DeactivatedAt: null, MfaEnrolledAt: not null })
            return AdminRefreshErrors.SessionEnded;
        if (!actor.Capabilities.HasFlag(AdminCapability.LicensingManager)) return ComplimentaryGrantErrors.AccessDenied;
        if (recentMfa && !new ProviderActor(caller.AdminId, session.MfaVerifiedAt).HasRecentMfa(now))
            return ComplimentaryGrantErrors.RecentMfa;
        return null;
    }
}
