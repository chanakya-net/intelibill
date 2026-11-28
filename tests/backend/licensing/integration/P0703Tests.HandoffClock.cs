using System.Data.Common;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0703Tests
{
    [Theory]
    [InlineData("window")]
    [InlineData("merchant")]
    [InlineData("lease")]
    [InlineData("restriction")]
    public async Task LicensingMutationAndSmtpHandoffAreOrderedAtFinalClockBoundary(string boundary)
    {
        var transition = new OnBeginSend();
        var lease = new P0201Clock { Now = Start };
        await using var paid = await P0308Fixture.StartAsync(database, Start, Settings(mailpit), services: services =>
        {
            services.AddKeyedSingleton<TimeProvider>("billing-email-lease-clock", lease);
            services.ConfigureDbContext<LicensingDbContext>(options => options.AddInterceptors(transition));
        });
        var package = await QueuePaidReminderAsync(paid);
        if (boundary == "restriction")
        {
            paid.At(Due.AddSeconds(1));
            var catalogue = paid.Checkout.Trial.Catalogue;
            using var owner = await catalogue.SignInOwnerAsync();
            using var security = await P0701Tests.SignInAdminWithAsync(
                catalogue,
                owner,
                "p0703-boundary@example.test",
                "security-administrator");
            await P0404Tests.SuspendAsync(security, $"/api/admin/accounts/{package.Account:D}/restrictions", "Account", null);
            paid.At(Due);
        }
        transition.Advance = () =>
        {
            if (boundary == "lease")
                lease.Now = lease.Now.AddMinutes(1);
            else if (boundary == "merchant")
                paid.Hosts.Fixture.Clock.Now = paid.Hosts.Fixture.Clock.Now.AddSeconds(15);
            else
                paid.At(boundary == "restriction" ? Due.AddSeconds(1) : Due.AddDays(6));
        };
        Assert.True(await DeliverAsync(paid));
        Assert.Null(transition.Advance);
        if (boundary == "lease")
        {
            Assert.True(await DeliverAsync(paid));
            Assert.Equal("Uncertain", await StateAsync(paid, package));
        }
        else
        {
            Assert.Equal("RetryPending", await StateAsync(paid, package));
            Assert.Equal(1L, await paid.CountAsync("notification_email_events", "reason = 'BeforeSubmission'"));
        }
        Assert.Empty(await mailpit.ReadAsync(package.Payer.Email));
    }

    private sealed class OnBeginSend : DbCommandInterceptor
    {
        internal Action? Advance { get; set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Advance is { } advance && command.CommandText.Contains("public.advance_notice_email", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is "Sending"))
            {
                Advance = null;
                advance();
            }
            return ValueTask.FromResult(result);
        }
    }
}
