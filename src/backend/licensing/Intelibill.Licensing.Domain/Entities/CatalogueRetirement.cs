namespace Intelibill.Licensing.Domain.Entities;

public sealed record CatalogueRetirement(Guid VersionId, Guid PlanId, Guid AdminId, Guid OperationKey,
    int ExpectedVersion, string Reason, DateTimeOffset RetiredAt);

public sealed record WithdrawnCatalogueOffer(Guid VersionId);
