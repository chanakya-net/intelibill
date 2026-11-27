namespace Intelibill.Application.Features.Hsn.Queries.LookupHsn;

public sealed record LookupHsnResult(
    string[] HsnCodes,
    HsnTaxScenarioResult[] TaxScenarios);

public sealed record HsnTaxScenarioResult(string Condition, string TaxPercentage);
