using ErrorOr;
using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class TillCloseReview : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid CloseId { get; private set; }
    public Guid ReviewedBy { get; private set; }
    public DateTimeOffset ReviewedAt { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    private TillCloseReview() { }
    public static ErrorOr<TillCloseReview> Record(TillClose close, Guid actorId, DateTimeOffset now, string reason)
    {
        if (actorId == Guid.Empty || now < close.ClosedAt || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return Error.Validation("Till.ReviewInvalid", "A reviewer and reason are required after the count.");
        return new TillCloseReview { ShopId = close.ShopId, SessionId = close.SessionId, CloseId = close.Id,
            ReviewedBy = actorId, ReviewedAt = now, Reason = reason.Trim() };
    }
}
