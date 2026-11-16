using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0215Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0215Tests
{
    private static readonly JsonSerializerOptions ContractJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CancellationContractPinsPathsPolicyAndCompatibleStatus()
    {
        using var contract = LoadContract("subscription-cancellation-v1");
        using var status = LoadContract("subscription-status-v1");
        var root = contract.RootElement;
        Assert.Equal(SubscriptionCancellationsV1.Policy, root.GetProperty("policyVersion").GetString());
        Assert.Equal(SubscriptionCancellationsV1.PageSize, root.GetProperty("pageSize").GetInt32());
        var example = root.GetProperty("acceptanceExample");
        var acceptance = example.Deserialize<ScheduleSubscriptionCancellationV1>(ContractJson)!;
        Assert.True(acceptance.IsValid());
        Assert.True(JsonElement.DeepEquals(example, JsonSerializer.SerializeToElement(acceptance, ContractJson)));
        var account = Guid.NewGuid();
        var term = Guid.NewGuid();
        Assert.Equal(root.GetProperty("internalPath").GetString()!.Replace("{accountId}", account.ToString("D"), StringComparison.Ordinal)
            .Replace("{termId}", term.ToString("D"), StringComparison.Ordinal), SubscriptionCancellationsV1.Path(account, term));
        Assert.True(root.GetProperty("actions").EnumerateObject().All(action => SubscriptionCancellationsV1.IsAction(action.Value.GetString()!)));

        var legacy = status.RootElement.GetProperty("example").Deserialize<ShopSubscriptionStatusV1>()!;
        Assert.Null(legacy.Term!.Cancellation);
        var scheduled = status.RootElement.GetProperty("cancellationExample");
        Assert.True(JsonElement.DeepEquals(scheduled, JsonSerializer.SerializeToElement(
            scheduled.Deserialize<SubscriptionCancellationSummaryV1>(ContractJson), ContractJson)));
    }

    [Fact]
    public void MaximumSizePageFitsRelayLimit()
    {
        var shops = Enumerable.Range(0, SubscriptionCancellationsV1.PageSize)
            .Select(_ => new CancellationShopV1(Guid.NewGuid(), new string('क', SubscriptionCancellationsV1.MaximumShopNameLength)))
            .ToArray();
        var page = new SubscriptionCancellationDetailV1(1, Guid.NewGuid(), Guid.NewGuid(), 0, null, EndsAt,
            SubscriptionCancellationsV1.Policy, "Asia/Kolkata", "Paid", new(149900, "INR", Guid.NewGuid()),
            new string('a', 64), new string('b', 64), shops.Length + 1, shops, shops[^1].ShopId, true, true);
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(page, ContractJson).Length, 1,
            SubscriptionCancellationsV1.MaximumResponseBytes);
    }

    [Fact]
    public async Task SignedAcceptanceRejectsChangedBodyOrPathAndValidatesExplicitTerms()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var owner = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(owner.Client);
        var term = await SeedPaidAsync(fixture.Hosts, owner, account, owner);
        var path = CancellationPath(account, term);
        var preview = await PreviewAsync(owner, path);
        var command = JsonSerializer.SerializeToUtf8Bytes(Accept(preview, Guid.NewGuid()), ContractJson);
        await AssertSignaturesAsync(fixture, owner, account, term, command);
        await AssertInvalidAcceptanceAsync(owner, path, command);
        Assert.Equal(0L, await fixture.Trial.CountAsync("scheduled_subscription_changes"));
    }

    private static async Task AssertSignaturesAsync(P0213Fixture fixture, Merchant owner, Guid account, Guid term, byte[] command)
    {
        var path = SubscriptionCancellationsV1.Path(account, term);
        var signed = P0202Fixture.Sign(fixture.Hosts.Fixture, owner.UserId, SubscriptionCancellationsV1.Schedule, command, claims =>
        {
            claims["account_id"] = account;
            claims["path"] = path;
            claims["method"] = "POST";
        }) with { Method = HttpMethod.Post };
        using var changed = await P0202Fixture.SendAsync(fixture.LicensingApi, signed, [.. command, (byte)' '], path);
        using var moved = await P0202Fixture.SendAsync(fixture.LicensingApi, signed, path: SubscriptionCancellationsV1.Path(account, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, moved.StatusCode);
    }

    private static async Task AssertInvalidAcceptanceAsync(Merchant owner, string path, byte[] command)
    {
        var changes = new (string Field, JsonNode? Value, HttpStatusCode Status)[]
        {
            ("expectedVersion", null, HttpStatusCode.BadRequest), ("expectedVersion", -1, HttpStatusCode.BadRequest),
            ("expectedVersion", 1, HttpStatusCode.Conflict), ("confirmed", false, HttpStatusCode.BadRequest),
            ("acceptedEffectiveAt", EndsAt.AddSeconds(1), HttpStatusCode.Conflict),
            ("affectedShopCount", 2, HttpStatusCode.Conflict), ("affectedShopSetHash", new string('0', 64), HttpStatusCode.Conflict),
            ("sourceHash", new string('0', 64), HttpStatusCode.Conflict),
        };
        foreach (var (field, value, status) in changes)
        {
            var modified = JsonNode.Parse(command)!.AsObject();
            if (value is null) modified.Remove(field);
            else modified[field] = value;
            using var response = await owner.Client.PostAsJsonAsync(path, modified);
            Assert.Equal(status, response.StatusCode);
        }
    }

    [Fact]
    public async Task StatusRelayRejectsCancellationThatConflictsWithPaidBoundary()
    {
        var transport = new ConflictingCancellation();
        await using var host = await StartAsync(database, P0210Fixture.Start, inner => { transport.InnerHandler = inner; return transport; });
        var owner = await SignInAsync(host, ShopRole.Owner);
        var account = await CreateAccountAsync(owner.Client);
        await SeedPaidAsync(host, owner, account, owner);
        using var response = await ShopStatusAsync(owner.Client, owner.ShopId);
        Assert.False((await ReadAsync(response)).GetProperty("term").TryGetProperty("cancellation", out _));

        transport.Path = SubscriptionStatusV1.ShopPath(owner.ShopId);
        using var rejected = await ShopStatusAsync(owner.Client, owner.ShopId);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
    }

    private static JsonDocument LoadContract(string file) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectPaths.Root, $"tests/contracts/licensing/{file}.json")));

    private sealed class ConflictingCancellation : DelegatingHandler
    {
        internal string? Path { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            if (request.RequestUri?.AbsolutePath != Path || !response.IsSuccessStatusCode) return response;
            var status = (await response.Content.ReadFromJsonAsync<ShopSubscriptionStatusV1>(ct))!;
            response.Content.Dispose();
            response.Content = JsonContent.Create(status with
            {
                Term = status.Term! with
                {
                    GraceEndsAt = null,
                    Cancellation = new(Guid.NewGuid(), 1, EndsAt.AddDays(-1), SubscriptionCancellationsV1.Policy),
                },
            });
            return response;
        }
    }
}
