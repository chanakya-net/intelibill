using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Intelibill.Application.Features.Shops.TillSettings;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Repositories;
using Intelibill.Domain.Interfaces;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using static Intelibill.Integration.Tests.TillTestContext;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class TillSettingsTests(PostgreSqlTestFixture fixture)
{
    private static ShopTillSettingsService Settings(TillTestContext app) => new(app.Tills, new ShopRepository(app.Db),
        new MembershipAuthorityRepository(app.Db, new UnitOfWork(app.Db)),
        new Intelibill.Infrastructure.Services.Tills.TillSessionPolicy(Microsoft.Extensions.Options.Options.Create(app.Options), new ShopRepository(app.Db), app.Tills), app.Time);

    [Fact]
    public async Task Migration_PreservesPriorTillUsers_LeavesUnconfiguredShopsOff_AndMatchesModel()
    {
        var database = $"till_settings_{Guid.NewGuid():N}";
        await using (var admin = new Npgsql.NpgsqlConnection(fixture.MigratorConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new Npgsql.NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(fixture.MigratorConnectionString) { Database = database }.ConnectionString;
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);
        var migrations = db.Database.GetMigrations().ToArray();
        var current = Array.FindIndex(migrations, x => x.EndsWith("_AddOwnerTillSettings", StringComparison.Ordinal));
        await db.GetService<IMigrator>().MigrateAsync(migrations[current - 1]);
        var prior = Shop.Create("Prior till user", "Address", "City", "State", "560001", null, null, null);
        var fresh = Shop.Create("Never configured", "Address", "City", "State", "560001", null, null, null);
        var actor = User.CreateWithEmail($"migration-{Guid.NewGuid():N}@example.test", "hash", "Owner", "Name");
        db.AddRange(prior, fresh, actor, ShopMembership.Create(prior.Id, actor.Id, ShopRole.Owner, false),
            Till.Create(prior.Id, "Existing drawer", actor.Id, DateTimeOffset.UtcNow).Value);
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync();
        Assert.True((await db.ShopTillSettings.SingleAsync(x => x.ShopId == prior.Id)).Enabled);
        Assert.Equal(0, (await db.ShopTillSettings.SingleAsync(x => x.ShopId == prior.Id)).Version);
        Assert.False(await db.ShopTillSettings.AnyAsync(x => x.ShopId == fresh.Id));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Api_ActiveShopScopeOwnerOptInAndAuthoritativeRecovery_UseRuntimeRls()
    {
        using var factory = new ApiWebApplicationFactory(fixture);
        await factory.InitializeAsync();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var registered = await client.EnrollAsync(new { email = $"till-settings-{Guid.NewGuid():N}@example.test", password = "Pass123!Aa", firstName = "Setup", lastName = "Owner" });
        registered.EnsureSuccessStatusCode();
        var auth = await registered.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        async Task<Guid> CreateShop()
        {
            using var response = await client.PostAsJsonAsync("/api/shops", new { name = "Till setup", address = "Address", city = "City", state = "State", pincode = "560001" });
            response.EnsureSuccessStatusCode();
            var scope = await response.Content.ReadFromJsonAsync<JsonElement>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.GetProperty("accessToken").GetString());
            return scope.GetProperty("activeShopId").GetGuid();
        }
        var other = await CreateShop();
        var shop = await CreateShop();
        using var crossRead = await client.GetAsync($"/api/shops/{other}/till-settings");
        using var crossWrite = await client.PutAsJsonAsync($"/api/shops/{other}/till-settings", new { enabled = true, expectedVersion = 0, reason = "Wrong active shop", confirmed = true });
        Assert.Equal(HttpStatusCode.Forbidden, crossRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossWrite.StatusCode);
        var initial = (await client.GetFromJsonAsync<ShopTillSettingsDto>($"/api/shops/{shop}/till-settings"))!;
        Assert.False(initial.Available); Assert.False(initial.Enabled); Assert.Equal(0, initial.Version);
        var options = factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value;
        options.Enabled = true; options.AllowedShopIds = [shop];
        foreach (var incomplete in new object[]
        {
            new { expectedVersion = 0, reason = "Missing enabled", confirmed = true },
            new { enabled = true, reason = "Missing version", confirmed = true },
            new { enabled = true, expectedVersion = 0, reason = "Missing confirmation" }
        })
        {
            using var missing = await client.PutAsJsonAsync($"/api/shops/{shop}/till-settings", incomplete);
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        }
        using var enabled = await client.PutAsJsonAsync($"/api/shops/{shop}/till-settings", new { enabled = true, expectedVersion = 0, reason = "Count drawers daily", confirmed = true });
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        var saved = (await enabled.Content.ReadFromJsonAsync<ShopTillSettingsDto>())!;
        Assert.True(saved.Enabled); Assert.True(saved.Available); Assert.Equal(1, saved.Version);
        Assert.Equal(saved, await client.GetFromJsonAsync<ShopTillSettingsDto>($"/api/shops/{shop}/till-settings"));
        using var stale = await client.PutAsJsonAsync($"/api/shops/{shop}/till-settings", new { enabled = false, expectedVersion = 0, reason = "Outdated", confirmed = true });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("Till.SettingsVersionConflict", await stale.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var disabled = await client.PutAsJsonAsync($"/api/shops/{shop}/till-settings", new { enabled = false, expectedVersion = 1, reason = "All device queues checked", confirmed = true });
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.False((await disabled.Content.ReadFromJsonAsync<ShopTillSettingsDto>())!.Enabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpeningAndDisabling_SerializeAtCatalogAndReloadGate(bool openingWins)
    {
        var (shop, owner, cashier, _) = await SeedAsync(fixture.MigratorConnectionString);
        await using var setup = new TillTestContext(fixture.MigratorConnectionString, shop);
        var till = await setup.CreateAsync(shop, owner);
        await using var opener = new TillTestContext(fixture.MigratorConnectionString, shop);
        await using var changer = new TillTestContext(fixture.MigratorConnectionString, shop);
        await using var barrier = await TillPostgresBarrier.CreateAsync(fixture.MigratorConnectionString,
            openingWins ? "till_sessions" : "shop_till_setting_events", shop);
        Task<ErrorOr.ErrorOr<Intelibill.Application.Features.Tills.TillSessionDto>> Open() =>
            opener.Lifecycle.OpenAsync(new(shop, cashier, till.Id, cashier, 0m, "settings-race-open"), default);
        Task<ErrorOr.ErrorOr<ShopTillSettingsDto>> Disable() =>
            Settings(changer).UpdateAsync(new(shop, owner, shop, false, 0, "All device queues checked", true), default);
        Task<ErrorOr.ErrorOr<Intelibill.Application.Features.Tills.TillSessionDto>> opening;
        Task<ErrorOr.ErrorOr<ShopTillSettingsDto>> disabling;
        if (openingWins)
        {
            opening = Open();
            var pid = await barrier.WaitForArrivalAsync();
            disabling = Disable();
            await barrier.WaitForBlockedByAsync(pid);
        }
        else
        {
            disabling = Disable();
            var pid = await barrier.WaitForArrivalAsync();
            opening = Open();
            await barrier.WaitForBlockedByAsync(pid);
        }
        await barrier.ReleaseAsync();
        var opened = await opening.WaitAsync(TimeSpan.FromSeconds(15));
        var disabled = await disabling.WaitAsync(TimeSpan.FromSeconds(15));
        if (openingWins)
        {
            Assert.False(opened.IsError);
            Assert.Equal("Till.SettingsBlocked", disabled.FirstError.Code);
        }
        else
        {
            Assert.False(disabled.IsError);
            Assert.Equal("Till.Disabled", opened.FirstError.Code);
        }
        var final = Success(await Settings(setup).GetAsync(new(shop, owner, shop), default));
        Assert.False(!final.Enabled && final.OpenSessionCount != 0);
    }

    [Fact]
    public async Task Enable_WaitsForDisabledLegacySourceToCommit_ThenNewSourceRequiresSession()
    {
        var (shop, owner, _, _) = await SeedAsync(fixture.MigratorConnectionString, enabled: false);
        await using var source = new TillTestContext(fixture.MigratorConnectionString, shop);
        await using var transaction = await source.Db.Database.BeginTransactionAsync();
        Assert.Null(Success(await source.Posting.AcquireAsync(shop, owner, null, source.Clock.Today("Asia/Kolkata"))));
        await using var changer = new TillTestContext(fixture.MigratorConnectionString, shop);
        await changer.Db.Database.OpenConnectionAsync();
        var pid = ((Npgsql.NpgsqlConnection)source.Db.Database.GetDbConnection()).ProcessID;
        var enabling = Settings(changer).UpdateAsync(new(shop, owner, shop, true, 0, "Start after older postings", true), default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await source.Db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE {pid}=ANY(pg_blocking_pids(pid))) AS \"Value\"").SingleAsync(timeout.Token))
            await Task.Delay(10, timeout.Token);
        Assert.False(enabling.IsCompleted);
        await transaction.CommitAsync();
        Assert.True(Success(await enabling.WaitAsync(TimeSpan.FromSeconds(15))).Enabled);
        await using var next = new TillTestContext(fixture.MigratorConnectionString, shop);
        await using var nextTransaction = await next.Db.Database.BeginTransactionAsync();
        Assert.Equal("Till.SessionRequired", (await next.Posting.AcquireAsync(shop, owner, null, next.Clock.Today("Asia/Kolkata"))).FirstError.Code);
    }

    [Fact]
    public async Task FreshShop_DefaultsOff_ReleaseGateCannotBeBypassed_VersionAndAuditAreAuthoritative()
    {
        var (shop, owner, _, _) = await SeedAsync(fixture.MigratorConnectionString, enabled: false);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, shop);
        var service = Settings(app);
        var initial = Success(await service.GetAsync(new(shop, owner, shop), default));
        Assert.True(initial.Available); Assert.False(initial.Enabled); Assert.Equal(0, initial.Version);
        app.Options.Enabled = false;
        Assert.Equal("Till.SettingsUnavailable", (await service.UpdateAsync(new(shop, owner, shop, true, 0, "Start tracking", true), default)).FirstError.Code);
        app.Options.Enabled = true;
        var saved = Success(await service.UpdateAsync(new(shop, owner, shop, true, 0, "Start tracking", true), default));
        Assert.True(saved.Enabled); Assert.Equal(1, saved.Version);
        Assert.Equal("Till.SettingsVersionConflict", (await service.UpdateAsync(new(shop, owner, shop, false, 0, "Disable", true), default)).FirstError.Code);
        var audit = Assert.Single(await app.Db.ShopTillSettingEvents.Where(x => x.ShopId == shop).ToArrayAsync());
        Assert.False(audit.PreviousEnabled); Assert.True(audit.Enabled); Assert.Equal(owner, audit.ActorUserId); Assert.Equal("Start tracking", audit.Reason);
        Assert.Equal(saved, Success(await service.GetAsync(new(shop, owner, shop), default)));
    }

    [Fact]
    public async Task Update_RequiresLiveOwnerActiveScopeReasonAndConfirmation()
    {
        var (shop, owner, cashier, _) = await SeedAsync(fixture.MigratorConnectionString, enabled: false);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, shop);
        var service = Settings(app);
        Assert.Equal("Till.Forbidden", (await service.UpdateAsync(new(shop, cashier, shop, true, 0, "Enable", true), default)).FirstError.Code);
        Assert.Equal("Till.Forbidden", (await service.UpdateAsync(new(shop, owner, Guid.NewGuid(), true, 0, "Enable", true), default)).FirstError.Code);
        Assert.True((await service.UpdateAsync(new(shop, owner, shop, true, 0, "", true), default)).IsError);
        Assert.True((await service.UpdateAsync(new(shop, owner, shop, true, 0, "Enable", false), default)).IsError);
        var membership = await app.Db.ShopMemberships.SingleAsync(x => x.ShopId == shop && x.UserId == owner);
        membership.ChangeAuthority(ShopRole.Manager, MembershipStatus.Active);
        await app.Db.SaveChangesAsync();
        Assert.Equal("Till.Forbidden", (await service.UpdateAsync(new(shop, owner, shop, true, 0, "Enable", true), default)).FirstError.Code);
        Assert.Empty(await app.Db.ShopTillSettingEvents.Where(x => x.ShopId == shop).ToArrayAsync());
    }

    [Fact]
    public async Task Disable_BlocksOpenOverdueAndAwaitingReview_ThenRetainsHistoryAndReplay()
    {
        var (shop, owner, cashier, _) = await SeedAsync(fixture.MigratorConnectionString);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, shop);
        var service = Settings(app);
        var till = Success(await app.Lifecycle.CreateAsync(new(shop, owner, "Front", "original-create"), default));
        var opened = await app.OpenAsync(shop, cashier, till.Id);
        Assert.Equal(1, Success(await service.GetAsync(new(shop, owner, shop), default)).OpenSessionCount);
        Assert.Equal("Till.SettingsBlocked", (await service.UpdateAsync(new(shop, owner, shop, false, 0, "Stop tracking", true), default)).FirstError.Code);
        app.Time.Now = app.Time.Now.AddDays(1);
        Assert.Equal("Till.SettingsBlocked", (await service.UpdateAsync(new(shop, owner, shop, false, 0, "Stop tracking", true), default)).FirstError.Code);
        app.Time.Now = app.Time.Now.AddDays(-1);
        var closed = Success(await app.Lifecycle.CloseAsync(new(shop, cashier, opened.Id, 999m, "short one", opened.Version, "settings-close"), default));
        Assert.Equal(1, Success(await service.GetAsync(new(shop, owner, shop), default)).AwaitingReviewCount);
        Assert.Equal("Till.SettingsBlocked", (await service.UpdateAsync(new(shop, owner, shop, false, 0, "Stop tracking", true), default)).FirstError.Code);
        Success(await app.Lifecycle.ReviewAsync(new(shop, owner, opened.Id, "Count checked", closed.Version, "settings-review"), default));
        var disabled = Success(await service.UpdateAsync(new(shop, owner, shop, false, 0, "Stop tracking", true), default));
        Assert.False(disabled.Enabled);
        Assert.Equal("Till.Disabled", (await app.Lifecycle.OpenAsync(new(shop, cashier, till.Id, cashier, 0, "after-disable"), default)).FirstError.Code);
        Assert.Equal(opened.Id, Success(await app.Reads.SessionAsync(shop, owner, opened.Id, 1, 20, default)).Session.Id);
        Assert.Equal(till.Id, Success(await app.Lifecycle.CreateAsync(new(shop, owner, "Front", "original-create"), default)).Id);
    }

    [Fact]
    public async Task Disable_UnpostedEnvelopesAndOpenTillCasesBlock_ResolvedDispositionClearsBlocker()
    {
        var (shop, owner, _, _) = await SeedAsync(fixture.MigratorConnectionString);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, shop);
        var service = Settings(app);
        var prior = ReconciliationIssue.Create(shop, null, "pre-envelope", "older-device", ReconciliationIssueType.ArithmeticConflict,
            "ArithmeticConflict", "Unposted printed invoice", owner);
        app.Db.Add(prior); await app.Db.SaveChangesAsync();
        Assert.Equal(1, Success(await service.GetAsync(new(shop, owner, shop), default)).PendingOfflineCount);
        Assert.Equal("Till.SettingsBlocked", (await service.UpdateAsync(new(shop, owner, shop, false, 0, "Stop", true), default)).FirstError.Code);
        prior.Resolve(owner, app.Clock.UtcNow); await app.Db.SaveChangesAsync();
        Assert.Equal(0, Success(await service.GetAsync(new(shop, owner, shop), default)).PendingOfflineCount);
        var envelope = OfflineSubmission.Record(shop, "device", "pending", "A/2627/000001", new string('a', 64), "{}", app.Clock.UtcNow, app.Clock.UtcNow, OfflineSubmissionSource.Sync, owner, null, null, null, null);
        app.Db.Add(envelope); await app.Db.SaveChangesAsync();
        Assert.Equal(1, Success(await service.GetAsync(new(shop, owner, shop), default)).PendingOfflineCount);
        Assert.Equal("Till.SettingsBlocked", (await service.UpdateAsync(new(shop, owner, shop, false, 0, "Stop", true), default)).FirstError.Code);
        var issue = ReconciliationIssue.Create(shop, null, "pending", "device", ReconciliationIssueType.TillReview, "Till.ReviewRequired", "Review", owner, offlineSubmissionId: envelope.Id);
        app.Db.Add(issue); await app.Db.SaveChangesAsync();
        Assert.Equal(1, Success(await service.GetAsync(new(shop, owner, shop), default)).PendingOfflineCount);
        issue.Resolve(owner, app.Clock.UtcNow); await app.Db.SaveChangesAsync();
        Assert.Equal(0, Success(await service.GetAsync(new(shop, owner, shop), default)).PendingOfflineCount);
        Assert.False(Success(await service.UpdateAsync(new(shop, owner, shop, false, 0, "All queues checked", true), default)).Enabled);
    }
}
