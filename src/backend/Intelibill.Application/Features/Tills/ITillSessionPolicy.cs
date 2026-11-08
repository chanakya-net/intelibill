using ErrorOr;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.Tills;

public interface ITillSessionPolicy
{
    bool IsEnabled(Guid shopId);
    bool MixedTenderEnabled { get; }
    Task<ErrorOr<Success>> AuthorizeAsync(Guid shopId, Guid actorId, Guid cashierId, string? overrideReason, CancellationToken ct = default);
    ErrorOr<Success> ValidateTradingDate(TillSession session, DateOnly sourceDate, DateOnly today);
}
