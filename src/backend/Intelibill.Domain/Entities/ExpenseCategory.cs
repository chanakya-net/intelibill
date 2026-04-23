using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class ExpenseCategory : BaseEntity
{
    public Guid ShopId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    private ExpenseCategory() { }

    public static ExpenseCategory Create(Guid shopId, string name, DateTimeOffset createdAt)
    {
        return new ExpenseCategory
        {
            Id = Guid.NewGuid(),
            ShopId = shopId,
            Name = name.Trim(),
            CreatedAt = createdAt,
        };
    }
}
