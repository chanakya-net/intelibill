using ErrorOr;
using System.Text.Json.Serialization;

namespace Intelibill.Application.Common.Interfaces;

public interface IExternalHsnLookupService
{
    Task<ErrorOr<ApiResponse<ExternalHsnLookupResponse>>> LookupAsync(string productName, CancellationToken cancellationToken);
}

public sealed record ApiResponse<T>(
    bool Success,
    T? Data,
    string? Error);

public sealed record ExternalHsnLookupResponse(
    string Name,
    [property: JsonPropertyName("hsn")] string[] HsnCodes,
    ExternalHsnTaxScenario[] TaxScenarios);

public sealed record ExternalHsnTaxScenario(string Condition, string TaxPercentage);
