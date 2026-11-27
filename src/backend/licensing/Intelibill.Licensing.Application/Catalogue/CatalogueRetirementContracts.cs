using ErrorOr;

namespace Intelibill.Licensing.Application.Catalogue;

public sealed record RetirePlanVersion(Guid OperationKey, int ExpectedVersion, string? Reason, bool Confirmed);
public sealed record PlanRetirementView(Guid OperationKey, Guid PlanId, Guid VersionId, int ExpectedVersion,
    string Reason, DateTimeOffset RetiredAt);
public sealed record PlanRetirementSummary(string Reason, DateTimeOffset RetiredAt);

internal static class CatalogueRetirementErrors
{
    internal static Error Invalid => Error.Validation("Catalogue.RetirementInvalid",
        "Confirm retirement and provide a reason of 1 to 500 characters.");
    internal static Error Conflict => Error.Conflict("Catalogue.RetirementConflict",
        "The retirement request or published version changed. Review the current version.");
    internal static Error Missing => Error.NotFound("Catalogue.RetirementNotFound", "The retirement operation was not found.");
}
