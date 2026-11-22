using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0413dLicensingTests
{
    [Theory]
    [InlineData("count", false)]
    [InlineData("adjust", false)]
    [InlineData("count", true)]
    [InlineData("adjust", true)]
    [InlineData("cost", true)]
    public async Task Final_write_wait_rechecks_expiry_and_live_session(string action, bool revoke)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(source.Binding, end);
        await EnforceAsync(source.Shop);
        var batchVersion = await BatchVersionAsync(source.Batch);
        var receiptVersion = await ReceiptVersionAsync(source.Receipt);
        var before = await FactsAsync(source);

        await using var db = Open(Migrator);
        await using var held = await db.Database.BeginTransactionAsync(Ct);
        // Reads and supplier locks can finish. The UPDATE on this batch must wait at SaveChanges.
        await db.Database.ExecuteSqlAsync($"SELECT id FROM inventory_batches WHERE id = {source.Batch} FOR NO KEY UPDATE", Ct);
        var blocker = ((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID;
        var posting = PostAtFinalWriteAsync(client, source, action, batchVersion, receiptVersion);
        int waiter;
        try
        {
            waiter = await WaitForBlockedRequestAsync(posting, blocker);
            if (revoke)
                await RevokePostingSessionAsync(client);
            else
                _clock.Now = end;
        }
        finally
        {
            await held.RollbackAsync(Ct);
        }

        Assert.NotEqual(0, waiter);
        if (revoke)
            await AssertProblemAsync(await posting, HttpStatusCode.Unauthorized, "Auth.SessionInvalidated");
        else
            await AssertProblemAsync(await posting, HttpStatusCode.Forbidden, "Licence.Expired");
        Assert.Equal(before, await FactsAsync(source));
        Assert.Equal(batchVersion, await BatchVersionAsync(source.Batch));
        Assert.Equal(receiptVersion, await ReceiptVersionAsync(source.Receipt));
        _output.WriteLine("{0}: blocker PID {1}, final write waiter PID {2}, checked at {3:O}. Unchanged {4}.",
            action, blocker, waiter, _clock.Now, before);
    }

    private static Task<HttpResponseMessage> PostAtFinalWriteAsync(
        HttpClient client, Source source, string action, uint batchVersion, uint receiptVersion) => action switch
    {
        "count" => client.PostAsJsonAsync($"/api/inventory/batches/{source.Batch}/count",
            new { expectedVersion = batchVersion, countedQuantity = 7, reason = "Fixture recount", operationKey = NewKey() }, Ct),
        "adjust" => client.PostAsJsonAsync($"/api/inventory/batches/{source.Batch}/adjust",
            new { direction = "Increase", reason = "ReturnRestockCorrection", quantity = 1, notes = "Fixture restock" }, Ct),
        "cost" => CorrectAsync(client, source.Receipt, receiptVersion, 1100, NewKey()),
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    private async Task RevokePostingSessionAsync(HttpClient client)
    {
        var jwt = new JwtSecurityToken(client.DefaultRequestHeaders.Authorization!.Parameter);
        var sessionId = Guid.Parse(jwt.Claims.Single(claim => claim.Type == "sid").Value);
        await using var db = Open(Migrator);
        var session = await db.AuthSessions.SingleAsync(x => x.Id == sessionId, Ct);
        session.Revoke("Fixture device revocation", _clock.Now);
        await db.SaveChangesAsync(Ct);
    }
}
