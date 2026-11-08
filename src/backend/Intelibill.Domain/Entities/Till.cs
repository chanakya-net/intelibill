using ErrorOr;
using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class Till : BaseEntity
{
    public Guid ShopId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public Guid CreatedBy { get; private set; }
    private Till() { }
    public static ErrorOr<Till> Create(Guid shopId, string name, Guid actorId, DateTimeOffset now)
    {
        if (shopId == Guid.Empty || actorId == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            return Error.Validation("Till.NameInvalid", "A named till and valid shop and creator are required.");
        return new Till { ShopId = shopId, Name = name.Trim(), NormalizedName = name.Trim().ToUpperInvariant(), CreatedBy = actorId, CreatedAt = now };
    }
}
