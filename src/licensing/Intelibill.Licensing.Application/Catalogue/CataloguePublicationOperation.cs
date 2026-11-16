using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.Catalogue;

public sealed class CataloguePublicationOperation(ICatalogueDraftStore drafts, ICataloguePublicationStore store,
    ICataloguePublicationApproval approval, TimeProvider clock)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal Task<ErrorOr<CataloguePublicationView>> PrepareAsync(AdminSessionCaller caller, Guid? planId, Guid versionId,
        PrepareCataloguePublication? request, Func<CataloguePreparation, CancellationToken, Task<ErrorOr<string>>> snapshot,
        CancellationToken token) => drafts.InTransactionAsync<CataloguePublicationView>(async ct =>
    {
        if (await AuthorizeAsync(caller, ct) is { } denied) return denied;
        if (versionId == Guid.Empty || planId == Guid.Empty || request is not { ExpectedVersion: > 0, Reason: { } reason }
            || request.OperationKey == Guid.Empty || reason.Trim().Length is < 1 or > 500 || reason.Any(char.IsControl)
            || !ValidTrialBinding(planId, request.TrialPolicyVersionId))
            return CatalogueErrors.DraftInvalid;

        await store.LockOperationAsync(caller.AdminId, request.OperationKey, ct);
        var candidate = new CataloguePreparation(caller.AdminId, request.OperationKey, planId, versionId,
            request.ExpectedVersion, request.TrialPolicyVersionId, reason.Trim(), approval.Revision ?? "",
            "", "", clock.GetUtcNow());
        var existing = await store.ReadPreparationAsync(caller.AdminId, request.OperationKey, ct);
        if (existing is not null) return await ReplayPreviewAsync(caller, candidate, existing, ct);
        var facts = await snapshot(candidate, ct);
        if (await AuthorizeAsync(caller, ct) is { } expired) return expired;
        if (facts.IsError) return facts.Errors;
        var prepared = candidate with { Snapshot = facts.Value, SnapshotHash = Hash(facts.Value), PreparedAt = clock.GetUtcNow() };
        drafts.Add(prepared);
        return View(prepared, null);
    }, token);

    internal Task<ErrorOr<CataloguePublicationView>> ConfirmAsync(AdminSessionCaller caller, Guid? planId, Guid versionId,
        ConfirmCataloguePublication? request, Func<CataloguePreparation, CancellationToken, Task<ErrorOr<DateTimeOffset>>> publish,
        CancellationToken token) => drafts.InTransactionAsync<CataloguePublicationView>(async ct =>
    {
        if (await AuthorizeAsync(caller, ct) is { } denied) return denied;
        if (request is not { Confirmed: true, ExpectedVersion: > 0, SnapshotHash.Length: 64 } || request.OperationKey == Guid.Empty)
            return CatalogueErrors.DraftInvalid;
        await store.LockOperationAsync(caller.AdminId, request.OperationKey, ct);
        var prepared = await store.ReadPreparationAsync(caller.AdminId, request.OperationKey, ct);
        if (prepared is null) return CataloguePublicationErrors.Missing;
        if (prepared.PlanId != planId || prepared.VersionId != versionId || prepared.ExpectedVersion != request.ExpectedVersion
            || prepared.SnapshotHash != request.SnapshotHash) return CataloguePublicationErrors.Conflict;
        if (await AuthorizeAsync(caller, ct) is { } expired) return expired;
        var existing = await store.ReadPublicationAsync(versionId, ct);
        if (existing is { AdminId: var admin, OperationKey: var key } && admin == caller.AdminId && key == request.OperationKey)
            return View(prepared, existing.PublishedAt);
        if (existing is not null || approval.Revision != prepared.ConfigurationRevision)
            return CataloguePublicationErrors.Conflict;
        var result = await publish(prepared, ct);
        return result.IsError ? result.Errors : View(prepared, result.Value);
    }, token);

    public Task<ErrorOr<CataloguePublicationView>> RecoverAsync(AdminSessionCaller caller, Guid key, CancellationToken token) =>
        drafts.InTransactionAsync<CataloguePublicationView>(async ct =>
        {
            if (await AuthorizeAsync(caller, ct) is { } denied) return denied;
            var prepared = await store.ReadPreparationAsync(caller.AdminId, key, ct);
            if (prepared is null) return CataloguePublicationErrors.Missing;
            return await ReadViewAsync(prepared, ct);
        }, token);

    internal Task<Error?> AuthorizeAsync(AdminSessionCaller caller, CancellationToken token) =>
        CatalogueAuthority.EnterAsync(drafts, caller, clock, token, recentMfa: true);

    private static bool ValidTrialBinding(Guid? planId, Guid? trialPolicyVersionId) =>
        planId is null ? trialPolicyVersionId is null : trialPolicyVersionId is { } id && id != Guid.Empty;

    internal static string Hash(string snapshot) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));

    internal static CataloguePublicationView View(CataloguePreparation prepared, DateTimeOffset? publishedAt) =>
        new(prepared.OperationKey, prepared.PlanId, prepared.VersionId, prepared.ExpectedVersion, prepared.SnapshotHash,
            JsonSerializer.Deserialize<JsonElement>(prepared.Snapshot, Json), prepared.Reason, publishedAt);

    private async Task<ErrorOr<CataloguePublicationView>> ReplayPreviewAsync(AdminSessionCaller caller,
        CataloguePreparation candidate, CataloguePreparation existing, CancellationToken token)
    {
        if (await AuthorizeAsync(caller, token) is { } denied) return denied;
        if (existing.PlanId != candidate.PlanId || existing.VersionId != candidate.VersionId
            || existing.ExpectedVersion != candidate.ExpectedVersion || existing.TrialPolicyVersionId != candidate.TrialPolicyVersionId
            || existing.Reason != candidate.Reason) return CataloguePublicationErrors.Conflict;
        return await ReadViewAsync(existing, token);
    }

    private async Task<CataloguePublicationView> ReadViewAsync(CataloguePreparation prepared, CancellationToken token)
    {
        var publication = await store.ReadPublicationAsync(prepared.VersionId, token);
        return View(prepared, publication?.AdminId == prepared.AdminId && publication.OperationKey == prepared.OperationKey
            ? publication.PublishedAt : null);
    }
}
