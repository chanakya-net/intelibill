using ErrorOr;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Features.Shops.Commands.CreateShop;
using Intelibill.Application.Features.Shops.Commands.UpdateShop;
using Intelibill.Application.Features.Shops.Setup;
using Intelibill.Application.Features.Shops.TillSettings;
using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0408dLicensingTests
{
    [Fact]
    public async Task DirectSetupWritersRefuseAMismatchedActorOrScope()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        using var stranger = CreateClient(host);
        var shop = await CreateShopAsync(owner);
        await RegisterAsync(stranger);
        await ApplyAsync(await BindAsync(shop), _clock.Now.AddDays(1));
        await EnforceAsync(shop);
        var session = ReadSession(owner);
        var foreign = ReadSession(stranger);
        var before = await SourceFactsAsync(shop, session.UserId);

        var profile = await UpdateDirectAsync(host, owner, UpdateCommand(session.UserId, shop, foreign));
        var absent = await UpdateDirectAsync(host, owner, UpdateCommand(session.UserId, shop, null));
        var created = await CreateDirectAsync(host, owner, CreateCommand(session.UserId, foreign.SessionId, foreign));
        var wrongDevice = await CreateDirectAsync(host, owner, CreateCommand(session.UserId, Guid.NewGuid(), session));
        var preference = await DirectAsync(host, owner, services =>
            ActivatorUtilities.CreateInstance<UpdateShopSetupPreferencesCommandHandler>(services).HandleAsync(
                new(shop, session.UserId, Guid.NewGuid(), 0, false, false) { Session = session }, Ct));
        var till = await DirectAsync(host, owner, services =>
            ActivatorUtilities.CreateInstance<UpdateShopTillSettingsCommandHandler>(services).HandleAsync(
                new(shop, session.UserId, shop, true, 0, "Direct", true) { Session = foreign }, Ct));
        var expiry = await DirectAsync(host, owner, services =>
            services.GetRequiredService<ExpiryPolicyService>().UpdateAsync(shop, session.UserId, null,
                new(before.CatalogVersion, null, "Direct"), foreign, Ct));

        Assert.Equal("Auth.SessionInvalidated", profile.FirstError.Code);
        Assert.Equal("Auth.SessionInvalidated", absent.FirstError.Code);
        Assert.Equal("Auth.SessionInvalidated", created.FirstError.Code);
        Assert.Equal("Auth.SessionInvalidated", wrongDevice.FirstError.Code);
        Assert.Equal("ShopSetup.Forbidden", preference.FirstError.Code);
        Assert.Equal("Till.Forbidden", till.FirstError.Code);
        Assert.Equal("Expiry.OwnerRequired", expiry.FirstError.Code);
        Assert.Equal(before, await SourceFactsAsync(shop, session.UserId));
    }

    [Theory]
    [InlineData("PendingActivation", "Unrestricted", "Licence.PendingActivation")]
    [InlineData("Active", "Trading", "Licence.TradingRestricted")]
    public async Task DirectCreateRefusesExpansionFromARestrictedSource(string state, string restriction, string code)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        await ApplyAsync(await BindAsync(shop), _clock.Now.AddDays(1), state: state, restriction: restriction);
        await EnforceAsync(shop);
        var session = ReadSession(client);
        var before = await SourceFactsAsync(shop, session.UserId);

        var result = await CreateDirectAsync(host, client, CreateCommand(session.UserId, session.SessionId, session));

        Assert.Equal(code, result.FirstError.Code);
        Assert.Equal(before, await SourceFactsAsync(shop, session.UserId));
    }

    [Fact]
    public async Task DirectCreateByAnOperatingOwnerAddsAnUnlicensedShell()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        await ApplyAsync(await BindAsync(shop), _clock.Now.AddDays(1));
        await EnforceAsync(shop);
        var session = ReadSession(client);
        var before = await SourceFactsAsync(shop, session.UserId);

        var result = await CreateDirectAsync(host, client, CreateCommand(session.UserId, session.SessionId, session));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        Assert.Equal(before with { Memberships = before.Memberships + 1 }, await SourceFactsAsync(shop, session.UserId));
        await using var db = Open(Migrator);
        var created = await db.ShopMemberships.Where(x => x.UserId == session.UserId && x.ShopId != shop)
            .Select(x => x.ShopId).SingleAsync(Ct);
        Assert.False(await db.Set<ShopLicenseState>().AnyAsync(x => x.ShopId == created, Ct));
        Assert.Equal(0, await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM public.shop_affiliation_consent_events WHERE shop_id = {created}
            """).SingleAsync(Ct));
        Assert.True(await db.Suppliers.AnyAsync(x => x.ShopId == created && x.IsSystem, Ct));
    }

    [Fact]
    public async Task DirectProfileUpdateFollowsTheCurrentLicenceDecision()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddDays(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        var session = ReadSession(client);

        var permitted = await UpdateDirectAsync(host, client, UpdateCommand(session.UserId, shop, session));
        Assert.False(permitted.IsError, permitted.IsError ? permitted.FirstError.Code : null);
        await AssertSetupFactsAsync(shop, session.UserId, "Direct rename", 1);

        await ApplyAsync(binding, end, 2, restriction: "Trading");
        var before = await SourceFactsAsync(shop, session.UserId);
        var expansion = await UpdateDirectAsync(host, client,
            UpdateCommand(session.UserId, shop, session) with { Name = "Moved", Address = "New registered lane" });
        Assert.Equal("Licence.TradingRestricted", expansion.FirstError.Code);
        Assert.Equal(before, await SourceFactsAsync(shop, session.UserId));
        await using var db = Open(Migrator);
        Assert.Equal("42 MG Road", await db.Shops.Where(x => x.Id == shop).Select(x => x.Address).SingleAsync(Ct));

        var recovery = await UpdateDirectAsync(host, client,
            UpdateCommand(session.UserId, shop, session) with { Name = "Recovered name" });
        Assert.False(recovery.IsError, recovery.IsError ? recovery.FirstError.Code : null);
        await AssertSetupFactsAsync(shop, session.UserId, "Recovered name", 1);
    }

    private static Task<ErrorOr<Intelibill.Application.Features.Shops.DTOs.ShopDetailsDto>> UpdateDirectAsync(
        ApiWebApplicationFactory host, HttpClient caller, UpdateShopCommand command) =>
        DirectAsync(host, caller, services =>
            ActivatorUtilities.CreateInstance<UpdateShopCommandHandler>(services).HandleAsync(command, Ct));

    private static Task<ErrorOr<AccessScopeResult>> CreateDirectAsync(
        ApiWebApplicationFactory host, HttpClient caller, CreateShopCommand command) =>
        DirectAsync(host, caller, services =>
            ActivatorUtilities.CreateInstance<CreateShopCommandHandler>(services).HandleAsync(command, Ct));

    private static UpdateShopCommand UpdateCommand(Guid actor, Guid shop, SessionClaims? session) =>
        new(actor, shop, "Direct rename", "42 MG Road", "Bengaluru", "Karnataka", "560001", "Owner", "+919111111111", null)
        { Session = session };

    private static CreateShopCommand CreateCommand(Guid actor, Guid sessionId, SessionClaims? session) =>
        new(actor, sessionId, "Direct shell", "9 Hill Road", "Bengaluru", "Karnataka", "560002", null, null, null)
        { Session = session };

    private async Task<SourceFacts> SourceFactsAsync(Guid shop, Guid actor)
    {
        await using var db = Open(Migrator);
        var name = await db.Shops.Where(x => x.Id == shop).Select(x => x.Name).SingleAsync(Ct);
        var memberships = await db.ShopMemberships.CountAsync(x => x.UserId == actor, Ct);
        var projection = await db.Set<ShopLicenseState>().AsNoTracking().SingleAsync(x => x.ShopId == shop, Ct);
        var expiry = await ExpiryFactsAsync(shop);
        var settings = await db.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM shop_till_settings WHERE shop_id = {shop}").SingleAsync(Ct);
        var preferences = await db.Database.SqlQuery<int>(
            $"SELECT version AS \"Value\" FROM shop_setup_preferences WHERE shop_id = {shop}").SingleAsync(Ct);
        return new(name, memberships, projection.Revision, projection.State, expiry.Version, expiry.Revisions, settings, preferences);
    }

    private sealed record SourceFacts(string Name, int Memberships, long Revision, string State,
        long CatalogVersion, int ExpiryRevisions, int TillSettings, int PreferenceVersion);
}
