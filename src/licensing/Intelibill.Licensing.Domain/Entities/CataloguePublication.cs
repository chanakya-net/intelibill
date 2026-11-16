namespace Intelibill.Licensing.Domain.Entities;

public enum CatalogueSource
{
    Unknown = 0,
    DevelopmentFixture = 1,
    CommercialCandidate = 2,
}

/// <summary>The exact proposal bound to an administrator's operation key before confirmation.</summary>
public sealed record CataloguePreparation(Guid AdminId, Guid OperationKey, Guid? PlanId, Guid VersionId,
    int ExpectedVersion, Guid? TrialPolicyVersionId, string Reason, string ConfigurationRevision,
    string Snapshot, string SnapshotHash, DateTimeOffset PreparedAt);

/// <summary>Append-only confirmation of a retained preparation. A version can be published only once.</summary>
public sealed record CataloguePublication(Guid VersionId, Guid AdminId, Guid OperationKey, DateTimeOffset PublishedAt);

/// <summary>Only the customer-safe plan terms are stored here. Every superseded snapshot is retained.</summary>
public sealed record PublishedCatalogueOffer(Guid PlanId, Guid VersionId, int VersionNumber, bool DevelopmentFixture, string Snapshot);
