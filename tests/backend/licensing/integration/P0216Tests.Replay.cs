using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0216Tests
{
    [Fact]
    public async Task WithdrawalAtOrAfterEffectIsRefusedAndAppendsNothing()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var payer = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(payer.Client);
        var term = await SeedPaidAsync(fixture.Hosts, payer, account, payer);
        fixture.Hosts.StatusClock.Now = EndsAt.AddDays(-3);
        var cancellation = await ScheduleAsync(payer, account, term);
        var key = Guid.NewGuid();

        foreach (var at in new[] { EndsAt, EndsAt.AddTicks(10), EndsAt.AddDays(2) })
        {
            fixture.Hosts.StatusClock.Now = at;
            using var refused = await payer.Client.PostAsJsonAsync(WithdrawalPath(account, term), Withdraw(cancellation, key));
            await AssertProblemAsync(refused, HttpStatusCode.Conflict, "Billing.CancellationEffective");
            Assert.Equal(1L, await fixture.Trial.CountAsync("scheduled_subscription_changes"));
            Assert.Equal(1L, await fixture.Trial.CountAsync("subscription_cancellation_receipts"));
            var status = (await P0215Fixture.StatusAsync(payer)).GetProperty("term");
            Assert.Equal("Expired", status.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, status.GetProperty("graceEndsAt").ValueKind);
            Assert.Equal(cancellation.GetProperty("changeId").GetGuid(), status.GetProperty("cancellation").GetProperty("changeId").GetGuid());
        }

        using var recover = await payer.Client.GetAsync(new Uri(WithdrawalPath(account, term) + $"/operations/{key:D}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, recover.StatusCode);
    }

    [Fact]
    public async Task StaleOrMismatchedVersionIsRefused()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var payer = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(payer.Client);
        var term = await SeedPaidAsync(fixture.Hosts, payer, account, payer);
        fixture.Hosts.StatusClock.Now = EndsAt.AddDays(-3);
        var path = WithdrawalPath(account, term);
        var unscheduled = new { operationKey = Guid.NewGuid(), currentVersion = 1L, changeId = Guid.NewGuid(), acceptedEffectiveAt = EndsAt, confirmed = true };
        using (var nothing = await payer.Client.PostAsJsonAsync(path, unscheduled))
            await AssertProblemAsync(nothing, HttpStatusCode.Conflict, "Billing.CancellationConflict");

        var cancellation = await ScheduleAsync(payer, account, term);
        var command = JsonSerializer.SerializeToUtf8Bytes(Withdraw(cancellation, Guid.NewGuid()), ContractJson);
        var changes = new (string Field, JsonNode? Value, HttpStatusCode Status)[]
        {
            ("currentVersion", null, HttpStatusCode.BadRequest), ("currentVersion", -1, HttpStatusCode.BadRequest),
            ("currentVersion", 0, HttpStatusCode.BadRequest), ("currentVersion", 2, HttpStatusCode.BadRequest),
            ("currentVersion", 3, HttpStatusCode.Conflict), ("confirmed", false, HttpStatusCode.BadRequest),
            ("changeId", Guid.NewGuid(), HttpStatusCode.Conflict), ("acceptedEffectiveAt", EndsAt.AddSeconds(1), HttpStatusCode.Conflict),
        };
        foreach (var (field, value, status) in changes)
        {
            var modified = JsonNode.Parse(command)!.AsObject();
            if (value is null) modified.Remove(field);
            else modified[field] = value;
            using var response = await payer.Client.PostAsJsonAsync(path, modified);
            Assert.Equal(status, response.StatusCode);
        }

        await AssertSignaturesAsync(fixture, payer, account, term, command);
        Assert.Equal(1L, await fixture.Trial.CountAsync("scheduled_subscription_changes"));
        using (var first = await payer.Client.PostAsJsonAsync(path, Withdraw(cancellation, Guid.NewGuid())))
            await ReadAsync(first);
        using var second = await payer.Client.PostAsJsonAsync(path, Withdraw(cancellation, Guid.NewGuid()));
        await AssertProblemAsync(second, HttpStatusCode.Conflict, "Billing.CancellationConflict");
        Assert.Equal(2L, await fixture.Trial.CountAsync("scheduled_subscription_changes"));
    }

    [Fact]
    public async Task SameKeyRetryReplaysImmutableReceiptWithoutNewGraceOrPublication()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var payer = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(payer.Client);
        var term = await SeedPaidAsync(fixture.Hosts, payer, account, payer);
        fixture.Hosts.StatusClock.Now = EndsAt.AddDays(-3);
        var cancellation = await ScheduleAsync(payer, account, term);
        var key = Guid.NewGuid();
        var command = Withdraw(cancellation, key);
        using var first = await payer.Client.PostAsJsonAsync(WithdrawalPath(account, term), command);
        var receipt = await ReadAsync(first);
        Assert.Equal(2L, receipt.GetProperty("version").GetInt64());
        Assert.Equal(EndsAt, receipt.GetProperty("effectiveAt").GetDateTimeOffset());
        Assert.Equal(EndsAt.AddDays(-3), receipt.GetProperty("withdrawnAt").GetDateTimeOffset());
        var evidence = await EvidenceCountsAsync(fixture);

        foreach (var at in new[] { EndsAt.AddDays(-3), EndsAt.AddDays(2) })
        {
            fixture.Hosts.StatusClock.Now = at;
            using var repeated = await payer.Client.PostAsJsonAsync(WithdrawalPath(account, term), command);
            using var recovered = await payer.Client.GetAsync(new Uri(WithdrawalPath(account, term) + $"/operations/{key:D}", UriKind.Relative));
            Assert.True(JsonElement.DeepEquals(receipt, await ReadAsync(repeated)));
            Assert.True(JsonElement.DeepEquals(receipt, await ReadAsync(recovered)));
            Assert.Equal(evidence, await EvidenceCountsAsync(fixture));
            var status = (await P0215Fixture.StatusAsync(payer)).GetProperty("term");
            Assert.Equal(EndsAt.AddDays(7), status.GetProperty("graceEndsAt").GetDateTimeOffset());
        }
    }

    [Fact]
    public async Task AuthorizationPrecedesReplayAndOnlyTheWithdrawingActorCanRecover()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var payer = await SignInAsync(fixture.Hosts);
        var manager = await SignInAsync(fixture.Hosts);
        var shop = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(payer.Client);
        var term = await SeedPaidAsync(fixture.Hosts, payer, account, shop);
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            INSERT INTO billing_account_members(id, account_id, merchant_user_id, role, status, revision, created_at)
            VALUES (gen_random_uuid(), '{account:D}', '{manager.UserId:D}', 2, 1, 1, now())
            """);
        fixture.Hosts.StatusClock.Now = EndsAt.AddDays(-3);
        var scheduleKey = Guid.NewGuid();
        var schedulePath = P0215Tests.CancellationPath(account, term);
        using (var scheduled = await payer.Client.PostAsJsonAsync(schedulePath,
                   P0215Tests.Accept(await P0215Fixture.PreviewAsync(payer, schedulePath), scheduleKey)))
            await ReadAsync(scheduled);
        var cancellation = (await P0215Fixture.StatusAsync(shop)).GetProperty("term").GetProperty("cancellation");
        var path = WithdrawalPath(account, term);
        var key = Guid.NewGuid();
        var command = Withdraw(cancellation, key);
        using (var denied = await shop.Client.PostAsJsonAsync(path, command))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var reusedSchedule = await payer.Client.PostAsJsonAsync(path, Withdraw(cancellation, scheduleKey)))
            await AssertProblemAsync(reusedSchedule, HttpStatusCode.Conflict, "Billing.OperationPayloadMismatch");

        using var accepted = await manager.Client.PostAsJsonAsync(path, command);
        var receipt = await ReadAsync(accepted);
        var operationPath = new Uri(path + $"/operations/{key:D}", UriKind.Relative);
        using (var wrongActor = await payer.Client.GetAsync(operationPath))
            Assert.Equal(HttpStatusCode.NotFound, wrongActor.StatusCode);
        using (var replayedByOther = await payer.Client.PostAsJsonAsync(path, command))
            await AssertProblemAsync(replayedByOther, HttpStatusCode.Conflict, "Billing.OperationPayloadMismatch");
        using (var asSchedule = await manager.Client.GetAsync(new Uri(schedulePath + $"/operations/{key:D}", UriKind.Relative)))
            Assert.Equal(HttpStatusCode.NotFound, asSchedule.StatusCode);
        using (var scheduleAsWithdrawal = await payer.Client.GetAsync(new Uri(path + $"/operations/{scheduleKey:D}", UriKind.Relative)))
            Assert.Equal(HttpStatusCode.NotFound, scheduleAsWithdrawal.StatusCode);
        using (var wrongTerm = await manager.Client.GetAsync(new Uri(WithdrawalPath(account, Guid.NewGuid()) + $"/operations/{key:D}", UriKind.Relative)))
            Assert.Equal(HttpStatusCode.NotFound, wrongTerm.StatusCode);
        using (var recovered = await manager.Client.GetAsync(operationPath))
            Assert.True(JsonElement.DeepEquals(receipt, await ReadAsync(recovered)));

        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE billing_account_members SET status = 2, revision = revision + 1 WHERE merchant_user_id = '{manager.UserId:D}'
            """);
        using var removedRead = await manager.Client.GetAsync(operationPath);
        using var removedReplay = await manager.Client.PostAsJsonAsync(path, command);
        Assert.Equal(HttpStatusCode.Forbidden, removedRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, removedReplay.StatusCode);
    }

    private static readonly JsonSerializerOptions ContractJson = new(JsonSerializerDefaults.Web);

    private static async Task AssertSignaturesAsync(P0213Fixture fixture, Merchant owner, Guid account, Guid term, byte[] command)
    {
        var path = SubscriptionCancellationsV1.WithdrawalPath(account, term);
        var signed = P0202Fixture.Sign(fixture.Hosts.Fixture, owner.UserId, SubscriptionCancellationsV1.Withdraw, command, claims =>
        {
            claims["account_id"] = account;
            claims["path"] = path;
            claims["method"] = "POST";
        }) with { Method = HttpMethod.Post };
        using var changed = await P0202Fixture.SendAsync(fixture.LicensingApi, signed, [.. command, (byte)' '], path);
        using var moved = await P0202Fixture.SendAsync(fixture.LicensingApi, signed,
            path: SubscriptionCancellationsV1.WithdrawalPath(account, Guid.NewGuid()));
        using var asSchedule = await P0202Fixture.SendAsync(fixture.LicensingApi, signed, path: SubscriptionCancellationsV1.Path(account, term));
        Assert.Equal(HttpStatusCode.Unauthorized, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, moved.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asSchedule.StatusCode);
    }

    internal static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"{(int)response.StatusCode}: {body}");
        Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
    }

    private static async Task<string> EvidenceCountsAsync(P0213Fixture fixture) => string.Join(',',
        await fixture.Trial.CountAsync("scheduled_subscription_changes"),
        await fixture.Trial.CountAsync("subscription_cancellation_receipts"),
        await fixture.Trial.CountAsync("entitlement_snapshots"),
        await fixture.SourceScalarAsync("SELECT max(revision) FROM entitlement_shop_heads"));
}
