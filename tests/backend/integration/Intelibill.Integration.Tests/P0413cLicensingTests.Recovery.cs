using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0413cLicensingTests
{
    [Theory]
    [InlineData("shipment", "supplier-return")]
    [InlineData("credit", "supplier-return-credit")]
    [InlineData("receipt", "purchase-receipt-reversal")]
    public async Task Committed_completion_recovers_only_with_current_recovery_authority(string operation, string slug)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        using var foreign = CreateClient(host);
        var source = await SeedAsync(client);
        await CreateShopAsync(foreign);
        var (route, body) = await PrepareAsync(client, source, operation);
        var payload = JsonSerializer.SerializeToNode(body, JsonSerializerOptions.Web)!;
        var key = payload["operationKey"]!.GetValue<string>();
        var original = await PostAsync(client, route, body);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(source.Binding, end);
        await EnforceAsync(source.Shop);
        _clock.Now = end;
        if (operation == "shipment")
            await PostAsync(client, $"/api/supplier-returns/{original.GetProperty("returnId").GetGuid()}/credit", Credit(original, NewKey()));

        var target = operation == "credit" ? original.GetProperty("returnId").GetGuid()
            : operation == "receipt" ? source.Receipt : source.Supplier;
        var lookup = $"/api/financial-operations/{slug}/{key}";
        var before = await FactsAsync(source.Shop);
        await AssertRecoveryAsync(client, foreign, route, body, lookup, slug, key, target, original, payload);
        await AssertLookupRacesSecurityAsync(client, source, end, lookup);
        await AssertProblemAsync(await client.PostAsJsonAsync(route, body, Ct), HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        Assert.Equal(before, await FactsAsync(source.Shop));
    }

    private static async Task AssertRecoveryAsync(HttpClient client, HttpClient foreign, string route, object body,
        string lookup, string slug, string key, Guid target, JsonElement original, JsonNode payload)
    {
        var saved = await GetAsync(client, lookup);
        Assert.Equal(slug, saved.GetProperty("operationType").GetString());
        Assert.Equal(key, saved.GetProperty("operationKey").GetString());
        Assert.Equal(target, saved.GetProperty("targetId").GetGuid());
        Assert.True(JsonElement.DeepEquals(original, saved.GetProperty("response")));
        Assert.True(JsonElement.DeepEquals(original, await PostAsync(client, route, body)));
        await AssertProblemAsync(await foreign.GetAsync(lookup, Ct), HttpStatusCode.NotFound, "Operation.NotFound");
        payload["reason"] = "Changed instruction";
        await AssertProblemAsync(await client.PostAsJsonAsync(route, payload, Ct),
            HttpStatusCode.Conflict, "Operation.PayloadMismatch");
    }

    private async Task AssertLookupRacesSecurityAsync(HttpClient client, Source source, DateTimeOffset end, string lookup)
    {
        await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "entitlement_snapshot_inbox", source.Shop);
        await using var delivery = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        await delivery.Database.OpenConnectionAsync(Ct);
        var projection = ApplyAsync(source.Binding, end.AddHours(1), 2, connection: delivery, securityEffectiveAt: _clock.Now);
        var blocker = await barrier.WaitForArrivalAsync();
        Assert.Equal(((NpgsqlConnection)delivery.Database.GetDbConnection()).ProcessID, blocker);

        var reading = client.GetAsync(lookup, Ct);
        var waiter = await ObserveWaitAsync(reading, blocker, barrier, projection);
        await AssertProblemAsync(await reading, HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        Assert.NotEqual(0, waiter);
    }

    [Fact]
    public async Task Demoted_owner_cannot_replay_a_reversal_or_read_the_outcome()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        var (route, body) = await PrepareAsync(client, source, "receipt");
        await PostAsync(client, route, body);
        var before = await FactsAsync(source.Shop);
        await using var db = Open(Migrator);
        var membership = await db.ShopMemberships.SingleAsync(x => x.ShopId == source.Shop, Ct);
        var email = await db.Users.Where(x => x.Id == membership.UserId).Select(x => x.Email).SingleAsync(Ct);
        membership.SetRole(ShopRole.Staff);
        await db.SaveChangesAsync(Ct);
        var login = await PostAsync(client, "/api/auth/login/email", new { email, password = "Pass123!Aa" });
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("accessToken").GetString());

        await AssertProblemAsync(await client.PostAsJsonAsync(route, body, Ct), HttpStatusCode.Forbidden, "SupplierReturn.Forbidden");
        var payload = JsonSerializer.SerializeToElement(body);
        using var lookup = await client.GetAsync($"/api/financial-operations/purchase-receipt-reversal/{payload.GetProperty("operationKey").GetString()}", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, lookup.StatusCode);
        Assert.Equal(before, await FactsAsync(source.Shop));
    }
}
