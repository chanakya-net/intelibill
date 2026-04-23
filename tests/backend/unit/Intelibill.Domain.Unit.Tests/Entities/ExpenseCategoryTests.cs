using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class ExpenseCategoryTests
{
    [Fact]
    public void Create_WithValidData_ReturnsCategory()
    {
        var shopId = Guid.NewGuid();
        var category = ExpenseCategory.Create(shopId, "Office Supplies", DateTimeOffset.UtcNow);

        Assert.Equal("Office Supplies", category.Name);
        Assert.Equal(shopId, category.ShopId);
    }

    [Fact]
    public void Create_TrimsName()
    {
        var category = ExpenseCategory.Create(Guid.NewGuid(), "  Rent  ", DateTimeOffset.UtcNow);

        Assert.Equal("Rent", category.Name);
    }
}
