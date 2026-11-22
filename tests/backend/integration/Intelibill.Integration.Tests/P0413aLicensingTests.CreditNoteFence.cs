using System.Net;
using System.Net.Http.Json;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0413aLicensingTests
{
    [Fact]
    public async Task Existing_credit_note_void_does_not_commit_behind_a_waiting_response()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        var sale = await SellAsync(client);
        using var returned = await ReturnAsync(client, sale, new
        {
            operationKey = Guid.NewGuid().ToString(), payoutDestination = (int)ReturnPayoutDestination.CreditNote,
            items = new[] { new { saleItemId = sale.Line, quantity = 1m, lineType = (int)SaleLineType.Goods,
                condition = (int)SaleReturnCondition.Restockable, approvedRefundAmount = 100m } },
        });
        await ReadSuccessAsync(returned);
        await using var db = Open(Migrator);
        var code = await db.CreditNotes.Where(note => note.ShopId == shop).Select(note => note.Code).SingleAsync(Ct);
        _clock.Now = end;

        await using var holder = await HoldCreditNoteFenceAsync(binding);
        var response = client.PostAsJsonAsync($"/api/credit-notes/{code}/void", new { reason = "Duplicated credit note" }, Ct);
        var pid = ((NpgsqlConnection)holder.Database.GetDbConnection()).ProcessID;
        await WaitForCreditNoteWriterAsync(db, pid, response);
        var whileHeld = await db.CreditNotes.Where(note => note.ShopId == shop).Select(note => note.IsVoided).SingleAsync(Ct);
        await holder.Database.RollbackTransactionAsync(Ct);
        using var voided = await response;
        Assert.True(voided.StatusCode == HttpStatusCode.NoContent, await voided.Content.ReadAsStringAsync(Ct));
        Assert.False(whileHeld);
        Assert.True(await db.CreditNotes.Where(note => note.ShopId == shop).Select(note => note.IsVoided).SingleAsync(Ct));
    }

    private async Task<Intelibill.Infrastructure.Data.ApplicationDbContext> HoldCreditNoteFenceAsync(Binding binding)
    {
        var holder = Open(Migrator);
        await holder.Database.OpenConnectionAsync(Ct);
        await holder.Database.BeginTransactionAsync(Ct);
        await holder.Database.ExecuteSqlAsync(
            $"SELECT public.take_merchant_licence_fence({binding.Account}::uuid, {binding.Shop}, false)", Ct);
        return holder;
    }

    private static async Task WaitForCreditNoteWriterAsync(Intelibill.Infrastructure.Data.ApplicationDbContext db, int holder, Task request)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!request.IsCompleted)
        {
            var blocked = await db.Database.SqlQuery<bool>(
                $"SELECT EXISTS (SELECT FROM pg_stat_activity WHERE {holder} = ANY(pg_blocking_pids(pid))) AS \"Value\"")
                .SingleAsync(budget.Token);
            if (blocked) return;
            await Task.Delay(10, budget.Token);
        }
        Assert.Fail("The request completed before it waited on the fence.");
    }
}
