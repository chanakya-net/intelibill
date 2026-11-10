using ErrorOr;
using HsnFinder.Api.Models.Responses;

namespace HsnFinder.Api.Services;

public interface IHsnLookupService
{
    Task<ErrorOr<HsnLookupResponse>> LookupAsync(string productName, CancellationToken cancellationToken = default);
}
