using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0413dLicensingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Security_taking_effect_during_outcome_read_wins_over_hits_and_conflicts(bool lookup, bool mismatch)
    {
        var step = new OutcomeClockStep(_clock);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var configured = host.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IInterceptor>(step)));
        using var client = configured.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        IdentityTestMailbox.Factories.Add(client, configured);
        var source = await SeedAsync(client);
        var end = _clock.Now.AddMinutes(1);
        var effective = end.AddMinutes(1);
        await ApplyAsync(source.Binding, end, securityEffectiveAt: effective);
        await EnforceAsync(source.Shop);
        _clock.Now = end;
        var version = await ReceiptVersionAsync(source.Receipt);
        var key = NewKey();
        using var first = await CorrectAsync(client, source.Receipt, version, 1100, key);
        await ReadSuccessAsync(first);
        var committed = await FactsAsync(source);

        step.Arm(effective);
        var response = lookup
            ? await client.GetAsync($"/api/financial-operations/receipt-cost-correction/{(mismatch ? NewKey() : key)}", Ct)
            : await CorrectAsync(client, source.Receipt, version, mismatch ? 1150 : 1100, key);
        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        Assert.Equal(effective, _clock.Now);
        Assert.Equal(committed, await FactsAsync(source));
    }

    [Fact]
    public async Task Revoked_session_cannot_recover_an_existing_correction()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        var version = await ReceiptVersionAsync(source.Receipt);
        var key = NewKey();
        using var first = await CorrectAsync(client, source.Receipt, version, 1100, key);
        await ReadSuccessAsync(first);
        var committed = await FactsAsync(source);

        var jwt = new JwtSecurityToken(client.DefaultRequestHeaders.Authorization!.Parameter);
        var sessionId = Guid.Parse(jwt.Claims.Single(claim => claim.Type == "sid").Value);
        await using var db = Open(Migrator);
        var session = await db.AuthSessions.SingleAsync(x => x.Id == sessionId, Ct);
        session.Revoke("Fixture security action", _clock.Now);
        await db.SaveChangesAsync(Ct);

        using var replay = await CorrectAsync(client, source.Receipt, version, 1100, key);
        using var outcome = await client.GetAsync($"/api/financial-operations/receipt-cost-correction/{key}", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, outcome.StatusCode);
        Assert.Equal(committed, await FactsAsync(source));
    }

    [Fact]
    public async Task Demotion_and_membership_removal_invalidate_old_correction_authority()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        var source = await SeedAsync(owner);
        var version = await ReceiptVersionAsync(source.Receipt);
        var key = NewKey();
        using var created = await CorrectAsync(owner, source.Receipt, version, 1100, key);
        await ReadSuccessAsync(created);
        var before = await FactsAsync(source);

        await using var db = Open(Migrator);
        var membership = await db.ShopMemberships.SingleAsync(x => x.ShopId == source.Shop, Ct);
        membership.ChangeAuthority(ShopRole.Manager, MembershipStatus.Active);
        await db.SaveChangesAsync(Ct);
        await AssertRecoverySessionRejectedAsync(owner, source, version, key);

        var email = await db.Users.Where(x => x.Id == membership.UserId).Select(x => x.Email).SingleAsync(Ct);
        var login = await PostAsync(owner, "/api/auth/login/email", new { email, password = "Pass123!Aa" });
        owner.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("accessToken").GetString());
        using var manager = await CorrectAsync(owner, source.Receipt, version, 1100, key);
        Assert.Equal(HttpStatusCode.Forbidden, manager.StatusCode);

        membership.ChangeAuthority(ShopRole.Manager, MembershipStatus.Removed);
        await db.SaveChangesAsync(Ct);
        await AssertRecoverySessionRejectedAsync(owner, source, version, key);
        Assert.Equal(before, await FactsAsync(source));
    }

    private static async Task AssertRecoverySessionRejectedAsync(HttpClient client, Source source, uint version, string key)
    {
        using var replay = await CorrectAsync(client, source.Receipt, version, 1100, key);
        using var read = await client.GetAsync($"/api/financial-operations/receipt-cost-correction/{key}", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
    }

    private sealed class OutcomeClockStep(TestClock clock) : DbCommandInterceptor
    {
        private long _next;

        public void Arm(DateTimeOffset next) => Interlocked.Exchange(ref _next, next.UtcTicks);

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM operation_receipts", StringComparison.Ordinal) &&
                Interlocked.Exchange(ref _next, 0) is > 0 and var ticks)
                clock.Now = new DateTimeOffset(ticks, TimeSpan.Zero);
            return ValueTask.FromResult(result);
        }
    }
}
