namespace HsnFinder.Api.Models.Responses;

public sealed record HsnLookupResponse(
    string Name,
    string[] Hsn,
    TaxScenario[] TaxScenarios);
