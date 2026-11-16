using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.Catalogue;

public sealed record PrepareCataloguePublication(Guid OperationKey, int ExpectedVersion, Guid? TrialPolicyVersionId, string? Reason);
public sealed record ConfirmCataloguePublication(Guid OperationKey, int ExpectedVersion, string? SnapshotHash, bool Confirmed);
public sealed record CataloguePublicationView(Guid OperationKey, Guid? PlanId, Guid VersionId, int ExpectedVersion,
    string SnapshotHash, JsonElement Snapshot, string Reason, DateTimeOffset? PublishedAt);

public interface ICataloguePublicationApproval
{
    CatalogueSource DraftSource { get; }
    string? Revision { get; }
    bool? PublicFixtureFilter { get; }
    string? AccountingTreatmentReference { get; }
    bool Allows(CatalogueSource source);
    bool Approves(string snapshotHash, Guid versionId, string trialTerms, string? customerTerms = null,
        string? refundPolicy = null);
}

public interface ICataloguePublicationStore
{
    Task LockOperationAsync(Guid adminId, Guid key, CancellationToken token);
    Task<CataloguePreparation?> ReadPreparationAsync(Guid adminId, Guid key, CancellationToken token);
    Task<CataloguePublication?> ReadPublicationAsync(Guid versionId, CancellationToken token);
    Task<TrialPolicyVersion?> LockPublishedTrialAsync(CancellationToken token);
    Task<PlanVersion?> LockPublishedPlanAsync(Guid planId, CancellationToken token);
    Task FlushAsync(CancellationToken token);
}

internal static class CataloguePublicationErrors
{
    internal static Error Incomplete => Error.Validation("Catalogue.Incomplete", "Complete every commercial fact before preparing publication.");
    internal static Error Configuration => Error.Forbidden("Catalogue.PublicationNotApproved", "Publication is unavailable for this source or configuration.");
    internal static Error Mfa => Error.Forbidden("Catalogue.RecentMfaRequired", "Sign in again with MFA before publishing.");
    internal static Error Conflict => Error.Conflict("Catalogue.PublicationConflict", "The publication request or terms changed. Review the current version.");
    internal static Error TrialRequired => Error.Validation("Catalogue.PublishedTrialRequired", "Select the current published trial policy for this source.");
    internal static Error Missing => Error.NotFound("Catalogue.OperationNotFound", "The publication operation was not found.");
}
