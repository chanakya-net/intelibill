using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0410LicensingTests
{
    [Fact]
    public async Task Expiry_denies_old_open_draft_but_allows_counted_close()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        var owner = Guid.Parse(new JwtSecurityToken(token).Claims.Single(x => x.Type == "sub").Value);
        var options = host.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value;
        options.Enabled = true;
        options.AllowedShopIds = [shop];
        using var settings = await client.PutAsJsonAsync($"/api/shops/{shop}/till-settings", new { enabled = true, expectedVersion = 0, reason = "Daily counts", confirmed = true }, Ct);
        await ReadSuccessAsync(settings);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        using var created = await client.PostAsJsonAsync("/api/tills", new { name = "Front", operationKey = "create" }, Ct);
        var till = (await ReadSuccessAsync(created)).GetProperty("id").GetGuid();
        var draft = new { cashierId = owner, openingFloat = 1000m, operationKey = "old-draft" };
        using var opened = await client.PostAsJsonAsync($"/api/tills/{till}/sessions", new { cashierId = owner, openingFloat = 1000m, operationKey = "open" }, Ct);
        var session = await ReadSuccessAsync(opened);
        _clock.Now = end;
        options.Enabled = false;
        await AssertProblemAsync(await client.PostAsJsonAsync($"/api/tills/sessions/{session.GetProperty("id").GetGuid()}/close", new { countedCash = 1000m, expectedVersion = session.GetProperty("version").GetUInt32(), operationKey = "disabled-close" }, Ct), HttpStatusCode.Conflict, "Till.Disabled");
        options.Enabled = true;
        using var closed = await client.PostAsJsonAsync($"/api/tills/sessions/{session.GetProperty("id").GetGuid()}/close", new { countedCash = 1000m, expectedVersion = session.GetProperty("version").GetUInt32(), operationKey = "close" }, Ct);
        await ReadSuccessAsync(closed);
        using var replay = await client.PostAsJsonAsync($"/api/tills/{till}/sessions", new { cashierId = owner, openingFloat = 1000m, operationKey = "open" }, Ct);
        Assert.Equal(session.GetProperty("id").GetGuid(), (await ReadSuccessAsync(replay)).GetProperty("id").GetGuid());
        await AssertProblemAsync(await client.PostAsJsonAsync($"/api/tills/{till}/sessions", draft, Ct), HttpStatusCode.Forbidden, "Licence.Expired");
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/tills", new { name = "Another", operationKey = "old-create" }, Ct), HttpStatusCode.Forbidden, "Licence.Expired");
        await using var db = Open(Migrator);
        Assert.Equal(1, await db.TillSessions.CountAsync(x => x.ShopId == shop, Ct));
        Assert.Equal(1, await db.TillCloses.CountAsync(x => x.ShopId == shop, Ct));
    }
    [Fact]
    public async Task Open_waits_for_the_shop_licence_fence_before_any_business_write()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var owner = Guid.Parse(new JwtSecurityToken(client.DefaultRequestHeaders.Authorization!.Parameter!).Claims.Single(x => x.Type == "sub").Value);
        var options = host.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value;
        options.Enabled = true;
        options.AllowedShopIds = [shop];
        using var settings = await client.PutAsJsonAsync($"/api/shops/{shop}/till-settings", new { enabled = true, expectedVersion = 0, reason = "Daily counts", confirmed = true }, Ct);
        await ReadSuccessAsync(settings);
        await ApplyAsync(await BindAsync(shop), _clock.Now.AddHours(1));
        await EnforceAsync(shop);
        using var created = await client.PostAsJsonAsync("/api/tills", new { name = "Counter", operationKey = "create" }, Ct);
        var till = (await ReadSuccessAsync(created)).GetProperty("id").GetGuid();
        await using var holder = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        await using var tx = await holder.Database.BeginTransactionAsync(Ct);
        await holder.Database.ExecuteSqlAsync($"SELECT public.take_merchant_licence_fence(NULL::uuid, {shop}, false)", Ct);
        await AssertProblemAsync(await client.PostAsJsonAsync($"/api/tills/{till}/sessions", new { cashierId = owner, openingFloat = 0m, operationKey = "blocked" }, Ct), HttpStatusCode.Conflict, "Licence.Busy");
        await tx.RollbackAsync(Ct);
        await using var db = Open(Migrator);
        Assert.Equal(0, await db.TillSessions.CountAsync(x => x.ShopId == shop, Ct));
    }

    [Fact]
    public async Task Expired_staff_count_requires_manager_review_and_keeps_date_rules()
    {
        var (shop, owner, cashier, _) = await TillTestContext.SeedAsync(Migrator);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(await BindAsync(shop), end);
        await EnforceAsync(shop);
        await using var app = new TillTestContext(Migrator, shop, enforceLicence: true);
        app.Time.Now = _clock.Now;
        await app.Db.Database.OpenConnectionAsync(Ct);
        await app.Db.Database.ExecuteSqlAsync($"SELECT set_config('app.active_shop_id', {shop.ToString("D")}, false)", Ct);
        var till = await app.CreateAsync(shop, owner);
        var session = await app.OpenAsync(shop, cashier, till.Id);
        app.Time.Now = end.AddDays(1);
        var overdue = await app.Lifecycle.CloseAsync(new(shop, cashier, session.Id, 990m, "Counted", session.Version, "late"), Ct);
        Assert.Equal("Till.Overdue", overdue.FirstError.Code);
        app.Time.Now = end;
        var closed = TillTestContext.Success(await app.Lifecycle.CloseAsync(new(shop, cashier, session.Id, 990m, "Counted", session.Version, "close"), Ct));
        Assert.True(closed.AwaitingReview);
        var refused = await app.Lifecycle.ReviewAsync(new(shop, cashier, session.Id, "Checked", closed.Version, "staff-review"), Ct);
        Assert.Equal("Till.Forbidden", refused.FirstError.Code);
        var reviewed = TillTestContext.Success(await app.Lifecycle.ReviewAsync(new(shop, owner, session.Id, "Checked", closed.Version, "manager-review"), Ct));
        Assert.False(reviewed.AwaitingReview);
    }

}
