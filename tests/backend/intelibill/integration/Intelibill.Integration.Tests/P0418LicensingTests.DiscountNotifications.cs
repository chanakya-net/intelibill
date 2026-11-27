using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Intelibill.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Intelibill.Integration.Tests;

public sealed partial class P0418LicensingTests
{
    [Theory]
    [InlineData("create", 1)]
    [InlineData("replace", 2)]
    [InlineData("disable", 1)]
    public async Task Discount_notification_observes_committed_rules(string operation, int notifications)
    {
        var witness = new DiscountNotificationWitness(Migrator);
        var commit = new DiscountCommitProbe(witness);
        using var host = CreateDiscountHost(witness, commit);
        await host.InitializeAsync();
        using var client = CreateClient(host);
        await OpenEnrolledShopAsync(client, _clock.Now.AddMinutes(1));
        var original = operation == "create" ? (Guid?)null : await SeedDiscountAsync(client, witness);

        commit.Arm();
        using var response = await WriteDiscountAsync(client, operation, original);
        var id = (await ReadSuccessAsync(response)).GetProperty("id").GetGuid();
        for (var i = 0; i < notifications; i++)
        {
            var notice = await witness.ReadAsync();
            _output.WriteLine("{0} on independent PostgreSQL connection {1}: {2} notified, rows [{3}]",
                operation, notice.Backend, notice.Ids.Count, string.Join(", ", notice.Rules));
            Assert.Equal(notice.Ids.Order(), notice.Rules.Select(x => x.Id).Order());
            var rule = Assert.Single(notice.Rules, x => x.Id == id);
            Assert.Equal(operation != "disable", rule.Active);
            if (operation == "replace")
            {
                Assert.Equal(original, rule.Replaces);
                if (notice.Ids.Contains(original!.Value))
                {
                    var previous = Assert.Single(notice.Rules, x => x.Id == original);
                    Assert.False(previous.Active);
                    Assert.Equal(id, previous.ReplacedBy);
                }
            }
        }
    }

    [Fact]
    public async Task Discount_denied_at_trading_deadline_sends_no_notification()
    {
        var witness = new DiscountNotificationWitness(Migrator);
        using var host = CreateDiscountHost(witness);
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var end = _clock.Now.AddMinutes(1);
        var binding = await OpenEnrolledShopAsync(client, end);
        var before = await CountCapacityRowsAsync(binding.Shop);

        _clock.Now = end;
        var denied = await ObserveAsync(await WriteDiscountAsync(client, "create", null));

        Assert.Equal(new Observed(HttpStatusCode.Forbidden, "Licence.Expired"), denied);
        Assert.Equal(before, await CountCapacityRowsAsync(binding.Shop));
        await witness.AssertNoNotificationAsync();
    }

    [Fact]
    public async Task Discount_rolled_back_before_commit_sends_no_notification()
    {
        var witness = new DiscountNotificationWitness(Migrator);
        var commit = new DiscountCommitProbe(witness);
        using var host = CreateDiscountHost(witness, commit);
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenEnrolledShopAsync(client, _clock.Now.AddMinutes(1));
        var before = await CountCapacityRowsAsync(binding.Shop);

        commit.Arm(failCommit: true);
        var failed = await ObserveAsync(await WriteDiscountAsync(client, "create", null));

        Assert.Equal(new Observed(HttpStatusCode.InternalServerError, "Internal.Unexpected"), failed);
        Assert.Equal(before, await CountCapacityRowsAsync(binding.Shop));
        await witness.AssertNoNotificationAsync();
    }

    private ApiWebApplicationFactory CreateDiscountHost(DiscountNotificationWitness witness, DiscountCommitProbe? commit = null) =>
        CreateHost(configure: services =>
        {
            services.RemoveAll<IShopUpdatesNotifier>();
            services.AddSingleton<IShopUpdatesNotifier>(witness);
            if (commit is not null)
                services.AddSingleton<IInterceptor>(commit);
        });

    private static async Task<Guid> SeedDiscountAsync(HttpClient client, DiscountNotificationWitness witness)
    {
        using var created = await WriteDiscountAsync(client, "create", null);
        var id = (await ReadSuccessAsync(created)).GetProperty("id").GetGuid();
        await witness.ReadAsync();
        return id;
    }

    private static Task<HttpResponseMessage> WriteDiscountAsync(HttpClient client, string operation, Guid? original) =>
        operation switch
        {
            "create" => client.PostAsJsonAsync("/api/discounts", DiscountInput("New discount"), Ct),
            "replace" => client.PutAsJsonAsync($"/api/discounts/{original:D}", DiscountInput("Replacement discount"), Ct),
            "disable" => client.PostAsJsonAsync($"/api/discounts/{original:D}/disable", new { reason = "Retired" }, Ct),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };

    private sealed class DiscountCommitProbe(DiscountNotificationWitness witness) : DbTransactionInterceptor
    {
        private int _armed;

        public void Arm(bool failCommit = false) => Interlocked.Exchange(ref _armed, failCommit ? 2 : 1);

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            // Give the real Wolverine handler time to read on its own connection if the write announced
            // itself too early. A correct write can commit after the bounded wait and only then notify.
            var attempt = Interlocked.Exchange(ref _armed, 0);
            if (attempt == 0)
                return result;

            await witness.CanReadAsync(TimeSpan.FromSeconds(2), cancellationToken);
            if (attempt == 2)
                throw new InvalidOperationException("Fixture refuses the discount transaction commit.");
            return result;
        }
    }

    private sealed class DiscountNotificationWitness(string connection) : IShopUpdatesNotifier
    {
        private readonly Channel<DiscountNotice> _notices = Channel.CreateUnbounded<DiscountNotice>();

        public async Task NotifyShopUpdateAsync(string eventType, Guid shopId, IReadOnlyCollection<Guid> changedIds,
            DateTimeOffset occurredOn, CancellationToken cancellationToken = default)
        {
            if (eventType != "DiscountRuleChanged")
                return;

            await using var db = Open(connection);
            await db.Database.OpenConnectionAsync(cancellationToken);
            var rows = await db.DiscountRules.Where(x => x.ShopId == shopId && changedIds.Contains(x.Id))
                .Select(x => new DiscountFact(x.Id, x.IsActive, x.ReplacesRuleId, x.ReplacedByRuleId))
                .ToArrayAsync(cancellationToken);
            await _notices.Writer.WriteAsync(new(Pid(db), changedIds, rows), cancellationToken);
        }

        public async Task<DiscountNotice> ReadAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return await _notices.Reader.ReadAsync(timeout.Token);
        }

        public async Task<bool> CanReadAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            wait.CancelAfter(timeout);
            try
            {
                return await _notices.Reader.WaitToReadAsync(wait.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        public async Task AssertNoNotificationAsync() =>
            Assert.False(await CanReadAsync(TimeSpan.FromMilliseconds(500), Ct));
    }

    private sealed record DiscountNotice(int Backend, IReadOnlyCollection<Guid> Ids, DiscountFact[] Rules);
    private sealed record DiscountFact(Guid Id, bool Active, Guid? Replaces, Guid? ReplacedBy);
}
