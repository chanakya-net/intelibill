using Intelibill.Licensing.Application.Catalogue;
using Intelibill.Licensing.Domain.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Intelibill.Licensing.Infrastructure.Catalogue;

internal sealed class CataloguePublicationOptions
{
    public string? Mode { get; init; }
    public string? Revision { get; init; }
    public string? AccountingTreatmentReference { get; init; }
    public string? PriceBasis { get; init; }
    public string? TrialTermsVersion { get; init; }
    public string? CustomerTermsVersion { get; init; }
    public string? RefundPolicyVersion { get; init; }
    public Dictionary<string, string> ApprovedSnapshots { get; init; } = [];
}

internal sealed class CataloguePublicationApproval(IOptionsSnapshot<CataloguePublicationOptions> options,
    IHostEnvironment environment) : ICataloguePublicationApproval
{
    private readonly CataloguePublicationOptions settings = options.Value;
    private bool IsLocal => environment.IsDevelopment() || environment.IsEnvironment("Testing");

    public CatalogueSource DraftSource => IsLocal ? CatalogueSource.DevelopmentFixture
        : settings.Mode == "Live" ? CatalogueSource.CommercialCandidate : CatalogueSource.Unknown;

    public string? Revision => ValidReference(settings.Revision, 80);
    public string? AccountingTreatmentReference => ValidReference(settings.AccountingTreatmentReference, 120);

    public bool? PublicFixtureFilter => Allows(CatalogueSource.DevelopmentFixture) ? true
        : Allows(CatalogueSource.CommercialCandidate) ? false : null;

    public bool Allows(CatalogueSource source) => Revision is not null && settings.Mode switch
    {
        "LocalFixture" => IsLocal && source == CatalogueSource.DevelopmentFixture,
        "Live" => !IsLocal && source == CatalogueSource.CommercialCandidate,
        _ => false,
    };

    public bool Approves(string snapshotHash, Guid versionId, string trialTerms, string? customerTerms = null,
        string? refundPolicy = null)
    {
        if (settings.TrialTermsVersion != trialTerms) return false;
        if (customerTerms is not null && (settings.CustomerTermsVersion != customerTerms
            || settings.RefundPolicyVersion != refundPolicy || settings.PriceBasis != "TaxInclusiveFinal"))
            return false;
        return settings.Mode != "Live" || (settings.ApprovedSnapshots.TryGetValue(versionId.ToString("D"), out var approved)
            && approved == snapshotHash);
    }

    private static string? ValidReference(string? value, int limit) =>
        value is { Length: > 0 } && value.Length <= limit && !string.IsNullOrWhiteSpace(value)
            && !value.Any(char.IsControl) ? value : null;
}
