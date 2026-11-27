using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0406LicensingTests
{
    [Fact]
    public async Task Projection_requires_local_enrollment_and_fresh_authority()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddDays(3);
        await ApplyAsync(binding, end);
        _clock.Now = _clock.Now.AddHours(48);
        var historical = _clock.Now.AddDays(-2);
        await PostAsync(client, Inbound, Row("Observe", historical));
        Assert.Equal(new StockFacts(1, 1, 2, 1, 1, 0, 0), await FactsAsync(shop));

        await EnforceAsync(shop);
        await AssertProblemAsync(await client.PostAsJsonAsync(Inbound, Row("Unavailable", historical), Ct),
            HttpStatusCode.ServiceUnavailable, "Licence.VerificationUnavailable");
        await ApplyAsync(binding, end, 2, state: "PendingActivation");
        await AssertProblemAsync(await client.PostAsJsonAsync(Inbound, Row("Pending", historical), Ct),
            HttpStatusCode.Forbidden, "Licence.PendingActivation");
        Assert.Equal(new StockFacts(1, 1, 2, 1, 1, 0, 0), await FactsAsync(shop));
    }

    [Fact]
    public async Task Old_manifest_with_no_committed_rows_cannot_start_posting_at_expiry()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var (shop, _, end) = await OpenIntakeAsync(client);
        var request = new { operationKey = "first", items = new[] { Row("A", _clock.Now.AddDays(-1)) } };
        await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "operation_receipts", shop,
            "NEW.operation_type = 4 AND NEW.operation_key = '5:first:A'", "BEFORE INSERT");
        var posting = client.PostAsJsonAsync(Bulk, request, Ct);
        await CrossDeadlineAsync(barrier, end);

        using var response = await posting;
        Assert.Equal(HttpStatusCode.MultiStatus, response.StatusCode);
        var result = await ReadSuccessAsync(response);
        Assert.Equal(0, result.GetProperty("successCount").GetInt32());
        Assert.Equal("Licence.Expired", result.GetProperty("failed")[0].GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(new StockFacts(0, 0, 0, 0, 0, 0, 1), await FactsAsync(shop));
    }

    [Fact]
    public async Task Revoked_device_waiting_on_duplicate_row_cannot_recover_outcome()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        using var second = CreateClient(host);
        var (shop, _, _) = await OpenIntakeAsync(owner);
        var session = await SignInOnSecondDeviceAsync(second, shop, owner);
        var request = new { operationKey = "shared-key", items = new[] { Row("A", _clock.Now.AddDays(-1)) } };
        await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "inventory_batches", shop);

        var first = owner.PostAsJsonAsync(Bulk, request, Ct);
        var firstPid = await barrier.WaitForArrivalAsync();
        var retry = second.PostAsJsonAsync(Bulk, request, Ct);
        try
        {
            var waiter = await barrier.WaitForBlockedByAsync(firstPid);
            await using var db = Open(Migrator);
            var statement = await db.Database.SqlQuery<string>($"""SELECT query AS "Value" FROM pg_stat_activity WHERE pid = {waiter}""").SingleAsync(Ct);
            Assert.Contains("INSERT INTO operation_receipts", statement, StringComparison.Ordinal);
            _output.WriteLine("Replay reservation backend {0} waits for posting backend {1}.", waiter, firstPid);
            using var revoked = await owner.DeleteAsync($"/api/users/me/sessions/{session}", Ct);
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        }
        finally
        {
            await barrier.ReleaseAsync();
        }

        using var posted = await first;
        var outcome = await ReadSuccessAsync(posted);
        using var refused = await retry;
        var refusedBody = await refused.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain(outcome.GetProperty("succeeded")[0].GetProperty("result").GetProperty("inventoryBatchId").GetString()!, refusedBody);
        await AssertProblemAsync(refused, HttpStatusCode.Unauthorized, "Auth.SessionInvalidated");
        Assert.Equal(new StockFacts(1, 1, 2, 1, 1, 0, 2), await FactsAsync(shop));
    }

    private async Task<Guid> SignInOnSecondDeviceAsync(HttpClient device, Guid shop, HttpClient owner)
    {
        var ownerToken = new JwtSecurityToken(owner.DefaultRequestHeaders.Authorization!.Parameter);
        var userId = Guid.Parse(ownerToken.Subject);
        await using var db = Open(Migrator);
        var email = await db.Users.Where(user => user.Id == userId).Select(user => user.Email).SingleAsync(Ct);
        var login = await PostAsync(device, "/api/auth/login", new { identifier = email, password = "Pass123!Aa" });
        device.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("accessToken").GetString());
        var switched = await PostAsync(device, "/api/shops/switch", new { shopId = shop });
        var token = switched.GetProperty("accessToken").GetString();
        device.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return Guid.Parse(new JwtSecurityToken(token).Claims.Single(claim => claim.Type == "sid").Value);
    }
}
