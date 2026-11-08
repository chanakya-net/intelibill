using System.Net;
using ErrorOr;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Expiry;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Services.Expiry;
using Intelibill.Infrastructure.Services.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ExpiryDispositionMidnightTests(PostgreSqlTestFixture fixture) : ExpiryDispositionFixture(fixture)
{
    protected override ApiWebApplicationFactory Factory { get; } = new(fixture, services =>
    {
        services.RemoveAll<IBusinessClock>(); services.AddSingleton<MovingClock>();
        services.AddSingleton<IBusinessClock>(sp => new BusinessClock(sp.GetRequiredService<MovingClock>()));
        services.RemoveAll<IExpiryPolicyFence>(); services.AddScoped<IExpiryPolicyFence>(sp => new MovingFence(sp.GetRequiredService<ApplicationDbContext>(), sp.GetRequiredService<MovingClock>()));
    });
    private sealed class MovingClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public bool AdvanceOnFence { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class MovingFence(ApplicationDbContext db, MovingClock clock) : IExpiryPolicyFence
    {
        public async Task<ErrorOr<Success>> AcquireAsync(Guid shopId, bool exclusive, CancellationToken ct)
        {
            var result = await new ExpiryPolicyFence(db).AcquireAsync(shopId, exclusive, ct);
            if (!result.IsError && clock.AdvanceOnFence) { clock.Now = clock.Now.AddMinutes(1); clock.AdvanceOnFence = false; }
            return result;
        }
    }
    [Fact]
    public async Task ReturnAndVoid_UseTheSamePostingDateAsTheirPeriodFence_WhenMidnightPassesWhileWaiting()
    {
        using var shop = await OpenShopAsync();
        var sale = await SellGoodsAsync(shop, quantity: 5m);
        var clock = Factory.Services.GetRequiredService<MovingClock>();
        clock.Now = new(2026,11,30,18,29,59,TimeSpan.Zero); clock.AdvanceOnFence = true;
        var recovered = await RecoverAsync(shop, sale);
        Assert.Equal(new DateOnly(2026,11,30),(await ReturnAsync(recovered.Return)).BusinessDate);
        var root = await QueryAsync(db => db.BatchDispositionChanges.SingleAsync(x => x.Id == recovered.Root));
        Assert.Equal(new DateOnly(2026,11,30),root.BusinessDate);
        clock.Now = new(2026,12,31,18,29,59,TimeSpan.Zero); clock.AdvanceOnFence = true;
        using var response = await SendAsync(shop, HttpMethod.Post, $"/api/sales/returns/{recovered.Return}/void", new { reason = "Wrong recovery" });
        Assert.Equal(HttpStatusCode.NoContent,response.StatusCode);
        var reversed = await ReturnAsync(recovered.Return); Assert.Equal(new DateOnly(2026,12,31),reversed.VoidedBusinessDate);
        var reverse = await QueryAsync(db => db.BatchDispositionChanges.SingleAsync(x => x.RootChangeId == root.Id));
        Assert.Equal(new DateOnly(2026,12,31),reverse.BusinessDate);
        Assert.Equal(new DateOnly(2026,12,31),DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reverse.RecordedAt,TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")).DateTime));
    }
    [Fact]
    public async Task Disposal_UsesItsCapturedPostingDateForPeriodAndLoss_WhenMidnightPassesWhileWaiting()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m); var held = await RecoverAsync(shop, sale);
        var clock = Factory.Services.GetRequiredService<MovingClock>();
        clock.Now = new(2026,11,30,18,29,59,TimeSpan.Zero); clock.AdvanceOnFence = true;
        using var response = await DisposeAsync(shop,sale.BatchId,held.Root,held.Batch.Version);
        Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        var loss = await QueryAsync(db => db.InventoryAdjustments.SingleAsync(x => x.InventoryBatchId == sale.BatchId));
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(loss.PerformedAt,TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")).DateTime);
        Assert.Equal(new DateOnly(2026,11,30),day);
        var child = await QueryAsync(db => db.BatchDispositionChanges.SingleAsync(x => x.RootChangeId == held.Root));
        Assert.Equal(day,child.BusinessDate);
    }
}
