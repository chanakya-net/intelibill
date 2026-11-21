using System.Net.Http.Json;
using ErrorOr;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0406LicensingTests
{
    [Fact]
    public async Task Unkeyed_intake_reports_committed_row_when_second_posting_fence_is_busy()
    {
        Binding? binding = null;
        var rowGate = new PauseBeforeSecondRow(async (post, ct) =>
        {
            await using var blocker = Open(Migrator);
            await using var transaction = await blocker.Database.BeginTransactionAsync(ct);
            var grant = binding!;
            await blocker.Database.ExecuteSqlAsync(
                $"SELECT public.take_merchant_licence_fence({grant.Account}::uuid, {grant.Shop}, false)", ct);
            Assert.Equal(new StockFacts(1, 1, 2, 1, 1, 0, 0), await FactsAsync(grant.Shop));
            var result = await post();
            Assert.True(result.IsError);
            Assert.Equal("Licence.Busy", result.FirstError.Code);
            await transaction.RollbackAsync(ct);
            _output.WriteLine("Real PostgreSQL blocked B at the licence fence after A committed. Released before handoff.");
            return result;
        });
        using var host = CreateHost(rowGate.Install);
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var (shop, activeBinding, _) = await OpenIntakeAsync(client);
        binding = activeBinding;
        var performedAt = _clock.Now.AddDays(-1);

        using var response = await client.PostAsJsonAsync(Bulk, new { items = new[] { Row("A", performedAt), Row("B", performedAt) } }, Ct);
        var original = await AssertPartialAsync(response, "Licence.Busy");
        var summary = await ReadSuccessAsync(response);
        Assert.True(summary.GetProperty("failed")[0].GetProperty("retryable").GetBoolean());
        await AssertCommittedRowAsync(shop, original.GetProperty("result"));
    }

    private async Task AssertCommittedRowAsync(Guid shop, System.Text.Json.JsonElement result)
    {
        Assert.Equal(new StockFacts(1, 1, 2, 1, 1, 0, 0), await FactsAsync(shop));
        await using var db = Open(Migrator);
        var batch = await db.InventoryBatches.SingleAsync(x => x.ShopId == shop, Ct);
        var movement = await db.StockTransactions.SingleAsync(x => x.ShopId == shop, Ct);
        Assert.Equal("A", batch.BatchNumber);
        Assert.Equal(batch.Id, result.GetProperty("inventoryBatchId").GetGuid());
        Assert.Equal(movement.Id, result.GetProperty("stockTransactionId").GetGuid());
    }

    private sealed class PauseBeforeSecondRow(
        Func<Func<Task<ErrorOr<AddInventoryResultDto>>>, CancellationToken, Task<ErrorOr<AddInventoryResultDto>>> pause)
    {
        public void Install(IServiceCollection services)
        {
            var registration = services.Single(x => x.ServiceType == typeof(IInboundRowExecutor));
            services.Remove(registration);
            services.AddScoped<IInboundRowExecutor>(provider => new WrappedExecutor(
                (IInboundRowExecutor)ActivatorUtilities.CreateInstance(provider, registration.ImplementationType!), pause));
        }

        private sealed class WrappedExecutor(IInboundRowExecutor executor,
            Func<Func<Task<ErrorOr<AddInventoryResultDto>>>, CancellationToken, Task<ErrorOr<AddInventoryResultDto>>> pause)
            : IInboundRowExecutor
        {
            public Task<ErrorOr<AddInventoryResultDto>> ExecuteAsync(Guid shopId, Guid actorUserId,
                InboundRowOperation? operation, InboundInventoryLineInput row, SessionClaims? session, CancellationToken cancellationToken)
            {
                Task<ErrorOr<AddInventoryResultDto>> PostAsync() =>
                    executor.ExecuteAsync(shopId, actorUserId, operation, row, session, cancellationToken);

                return row.BatchNumber == "B" ? pause(PostAsync, cancellationToken) : PostAsync();
            }
        }
    }
}
