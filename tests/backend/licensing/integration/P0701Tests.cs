using System.Diagnostics;
using Intelibill.Domain.Enums;
using Intelibill.Licensing.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0701Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task TrialOwnerHasOneScheduledThreeDayNotice()
    {
        var extras = new Dictionary<string, string?>
        {
            ["Notifications:PlanInBackground"] = "true",
            ["Notifications:IntervalMilliseconds"] = "100",
            ["Notifications:AccountPageSize"] = "1",
            ["Notifications:TermPageSize"] = "1",
            ["Notifications:MemberPageSize"] = "1",
        };
        await using var delivery = await P0213Fixture.StartAsync(database, licensingExtras: extras);
        var trial = delivery.Trial;
        var owner = await SignInAsync(trial.Hosts, ShopRole.Owner);
        var payer = await SignInAsync(trial.Hosts);
        var account = await CreateAccountAsync(payer.Client);
        await trial.LinkAsync(account, payer.UserId, owner);

        // Also seed a second account to prove discovery pages beyond the first page (AccountPageSize=1).
        var secondOwner = await SignInAsync(trial.Hosts, ShopRole.Owner);
        var secondPayer = await SignInAsync(trial.Hosts);
        var secondAccount = await CreateAccountAsync(secondPayer.Client);
        await trial.LinkAsync(secondAccount, secondPayer.UserId, secondOwner);

        var applied = await P0210Tests.StartAndWaitAsync(owner, trial.Accept());
        Assert.Equal("Applied", applied.Status);
        var secondApplied = await P0210Tests.StartAndWaitAsync(secondOwner, trial.Accept());
        Assert.Equal("Applied", secondApplied.Status);

        var expectedDueAt = P0210Fixture.Start.AddDays(11);
        var count = await WaitForScheduledCountAsync(
            trial.LicensingOwner,
            account,
            $"user:{payer.UserId:D}",
            expectedDueAt);
        var secondCount = await WaitForScheduledCountAsync(
            trial.LicensingOwner,
            secondAccount,
            $"user:{secondPayer.UserId:D}",
            expectedDueAt);

        Assert.Equal(1L, count);
        Assert.Equal(1L, secondCount);

        // Revisiting after a new billing manager joins schedules that manager without duplicating the owner.
        var manager = await SignInAsync(trial.Hosts);
        await P0204Tests.InviteManagerAsync(payer.Client, account, manager.Client, manager.Email);
        var managerCount = await WaitForScheduledCountAsync(
            trial.LicensingOwner,
            account,
            $"user:{manager.UserId:D}",
            expectedDueAt);
        Assert.Equal(1L, managerCount);
        Assert.Equal(
            1L,
            await CountScheduledTrialThreeDayNoticesAsync(
                trial.LicensingOwner,
                account,
                $"user:{payer.UserId:D}",
                expectedDueAt));
    }

    internal static async Task<NotificationPassResult> RunPlannerPassAsync(
        IServiceProvider services,
        int accountPageSize = 20,
        int termPageSize = 50,
        int memberPageSize = 50)
    {
        await using var scope = services.CreateAsyncScope();
        var planner = scope.ServiceProvider.GetRequiredService<NotificationPlanner>();
        return await planner.PlanAllDueAsync(CancellationToken.None, accountPageSize, termPageSize, memberPageSize);
    }

    private static async Task<long> WaitForScheduledCountAsync(
        NpgsqlConnectionStringBuilder settings,
        Guid accountId,
        string recipientKey,
        DateTimeOffset expectedDueAt)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(15))
        {
            var current = await CountScheduledTrialThreeDayNoticesAsync(settings, accountId, recipientKey, expectedDueAt);
            if (current > 0)
                return current;
            await Task.Delay(100);
        }

        return await CountScheduledTrialThreeDayNoticesAsync(settings, accountId, recipientKey, expectedDueAt);
    }

    private static async Task<long> CountScheduledTrialThreeDayNoticesAsync(
        NpgsqlConnectionStringBuilder settings,
        Guid accountId,
        string recipientKey,
        DateTimeOffset expectedDueAt)
    {
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        await using (var exists = new NpgsqlCommand("SELECT to_regclass('public.notification_jobs')::text", connection))
        {
            var relation = await exists.ExecuteScalarAsync();
            if (relation is null or DBNull)
                return 0L;
        }

        await using var count = new NpgsqlCommand("""
            SELECT count(*)
            FROM public.notification_jobs
            WHERE account_id = $1
              AND scope_kind = 'Account'
              AND recipient_key = $2
              AND purpose = 'TrialReminder'
              AND threshold = 'TrialMinus3Days'
              AND due_at = $3
              AND status = 'Scheduled'
            """, connection);
        count.Parameters.AddWithValue(accountId);
        count.Parameters.AddWithValue(recipientKey);
        count.Parameters.AddWithValue(expectedDueAt);
        return (long)(await count.ExecuteScalarAsync())!;
    }
}
