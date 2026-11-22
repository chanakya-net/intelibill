using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0407LicensingTests
{
    [Theory]
    [InlineData("reserve-keyed")]
    [InlineData("complete-keyed")]
    [InlineData("save-unkeyed")]
    public async Task Receipt_waits_recheck_live_access_before_commit(string stage)
    {
        using var host = CreateHost();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var deadline = _clock.Now.AddMinutes(5);
        await ApplyAsync(await BindAsync(shop), deadline);
        await EnforceAsync(shop);
        var (order, line) = await PlaceOrderAsync(client);
        var before = await ReadFactsAsync(shop, order);
        var keyed = stage != "save-unkeyed";
        var payload = new
        {
            operationId = keyed ? Guid.NewGuid() : (Guid?)null,
            receivedAt = _clock.Now,
            lines = new[] { ReceiptLine(line, "LOT-A", 4), ReceiptLine(line, "LOT-B", 6) },
        };
        await using var barrier = await PauseReceiptAsync(shop, stage);
        var posting = client.PostAsJsonAsync($"/api/purchase-orders/{order}/receipts", payload, Ct);
        var waiter = await barrier.WaitForArrivalAsync();
        try
        {
            _clock.Now = deadline;
        }
        finally
        {
            await barrier.ReleaseAsync();
        }

        await AssertProblemAsync(await posting, HttpStatusCode.Forbidden, "Licence.Expired");
        Assert.Equal(before, await ReadFactsAsync(shop, order));
        _output.WriteLine($"PostgreSQL PID {waiter} waited on {barrier.BlockerPid} at {stage} until {deadline:O}; both lots rolled back.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Waiting_duplicate_rechecks_session_before_replaying_or_comparing_hash(bool revoke)
    {
        using var host = CreateHost();
        using var owner = CreateClient(host);
        using var second = CreateClient(host);
        var shop = await CreateShopAsync(owner);
        await ApplyAsync(await BindAsync(shop), _clock.Now.AddMinutes(5));
        await EnforceAsync(shop);
        var (order, line) = await PlaceOrderAsync(owner);
        await SignInAgainAsync(owner, second);
        var path = $"/api/purchase-orders/{order}/receipts";
        var key = Guid.NewGuid();
        var original = Receipt(line, "LOT-A", 4, key, _clock.Now);
        await using var barrier = await PauseReceiptAsync(shop, "complete-keyed");
        var first = owner.PostAsJsonAsync(path, original, Ct);
        var firstPid = await barrier.WaitForArrivalAsync();
        var duplicate = second.PostAsJsonAsync(path,
            revoke ? Receipt(line, "REPLAY-CHANGED", 4, key, _clock.Now) : original, Ct);
        try
        {
            var duplicatePid = await barrier.WaitForBlockedByAsync(firstPid);
            _output.WriteLine($"Duplicate PID {duplicatePid} waited on original PID {firstPid}.");
            if (revoke) await RevokeSessionAsync(second);
        }
        finally
        {
            await barrier.ReleaseAsync();
        }

        using var response = await first;
        var committed = await ReadSuccessAsync(response);
        using var retry = await duplicate;
        if (revoke)
            await AssertProblemAsync(retry, HttpStatusCode.Unauthorized, "Auth.SessionInvalidated");
        else
            Assert.True(JsonElement.DeepEquals(committed, await ReadSuccessAsync(retry)));
        using var facts = JsonDocument.Parse(await ReadFactsAsync(shop, order));
        Assert.Equal(1, facts.RootElement.GetProperty("receipts").GetArrayLength());
        Assert.Equal(1, facts.RootElement.GetProperty("operations").GetArrayLength());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Projection_update_and_receipt_serialize_on_the_same_fence(bool projectionFirst)
    {
        using var host = CreateHost();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var deadline = _clock.Now.AddHours(1);
        await ApplyAsync(binding, deadline);
        await EnforceAsync(shop);
        var (order, line) = await PlaceOrderAsync(client);
        var path = $"/api/purchase-orders/{order}/receipts";
        var before = await ReadFactsAsync(shop, order);
        Task<HttpResponseMessage> PostAsync(string batch) =>
            client.PostAsJsonAsync(path, Receipt(line, batch, 4, Guid.NewGuid(), _clock.Now), Ct);
        Task RestrictAsync() => ApplyAsync(binding, deadline, revision: 2, restriction: "Trading");

        await using var barrier = projectionFirst
            ? await TillPostgresBarrier.CreateAsync(Migrator, "shop_license_states", shop, "NEW.revision = 2", "AFTER UPDATE")
            : await PauseReceiptAsync(shop, "complete-keyed");
        var projection = projectionFirst ? RestrictAsync() : null;
        var receipt = projectionFirst ? null : PostAsync("FIRST");
        var firstPid = await barrier.WaitForArrivalAsync();
        try
        {
            if (projectionFirst) receipt = PostAsync("FIRST");
            else projection = RestrictAsync();
            var secondPid = await barrier.WaitForBlockedByAsync(firstPid);
            _output.WriteLine($"Projection first={projectionFirst}: waiter {secondPid}, holder {firstPid}.");
        }
        finally
        {
            await barrier.ReleaseAsync();
        }

        await projection!;
        if (projectionFirst)
            await AssertProblemAsync(await receipt!, HttpStatusCode.Forbidden, "Licence.TradingRestricted");
        else
            await ReadSuccessAsync(await receipt!);

        var after = await ReadFactsAsync(shop, order);
        if (projectionFirst) Assert.Equal(before, after);
        await AssertProblemAsync(await PostAsync("LATER"), HttpStatusCode.Forbidden, "Licence.TradingRestricted");
        Assert.Equal(after, await ReadFactsAsync(shop, order));
    }

    private Task<TillPostgresBarrier> PauseReceiptAsync(Guid shop, string stage) => stage switch
    {
        "reserve-keyed" => TillPostgresBarrier.CreateAsync(Migrator, "operation_receipts", shop,
            "NEW.operation_type = 13", "BEFORE INSERT"),
        "complete-keyed" => TillPostgresBarrier.CreateAsync(Migrator, "operation_receipts", shop,
            "NEW.operation_type = 13", "AFTER UPDATE"),
        _ => TillPostgresBarrier.CreateAsync(Migrator, "purchase_order_receipts", shop),
    };

    private static async Task SignInAgainAsync(HttpClient original, HttpClient second)
    {
        var jwt = new JwtSecurityToken(original.DefaultRequestHeaders.Authorization!.Parameter);
        var email = jwt.Claims.Single(x => x.Type == "email").Value;
        using var response = await second.PostAsJsonAsync("/api/auth/login/email", new { email, password = "Pass123!Aa" }, Ct);
        var token = (await ReadSuccessAsync(response)).GetProperty("accessToken").GetString();
        second.DefaultRequestHeaders.Authorization = new("Bearer", token);
    }

    private async Task RevokeSessionAsync(HttpClient client)
    {
        var jwt = new JwtSecurityToken(client.DefaultRequestHeaders.Authorization!.Parameter);
        var sessionId = Guid.Parse(jwt.Claims.Single(x => x.Type == "sid").Value);
        await using var db = Open(Migrator);
        var session = await db.AuthSessions.SingleAsync(x => x.Id == sessionId, Ct);
        session.Revoke("Fixture device revocation", _clock.Now);
        await db.SaveChangesAsync(Ct);
    }
}
