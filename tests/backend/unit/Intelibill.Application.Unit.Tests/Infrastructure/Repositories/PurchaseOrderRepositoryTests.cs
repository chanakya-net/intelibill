using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Application.Unit.Tests.Infrastructure.Repositories;

public sealed class PurchaseOrderRepositoryTests
{
    [Fact]
    public void GetByShopQuery_TranslatesOpenWorkFirstSortForPostgres()
    {
        using var context = CreateContext();

        var shopId = Guid.NewGuid();
        var query = context.PurchaseOrders
            .Include(po => po.Lines)
            .Where(po => po.ShopId == shopId)
            .OrderBy(po => po.Status == PurchaseOrderStatus.Draft ? 0 : 99)
            .ThenByDescending(po => po.CreatedAt)
            .ThenByDescending(po => po.Id)
            .Skip(0)
            .Take(20);

        var sql = query.ToQueryString();

        Assert.Contains("CASE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("created_at", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=intelibill_tests;Username=test;Password=test")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(options);
    }
}
