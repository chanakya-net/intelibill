using Intelibill.Application.Common.Licensing.Offline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0501LicensingTests
{
    /// <summary>
    /// A signer that gives up after the wait, at reservation. When the refresh credential, the lease,
    /// the licence head or the clock passed its bound meanwhile, the caller gets that mandatory refusal,
    /// never the signing failure a reservation may answer with a legacy lease. The still-authorized
    /// control keeps <c>OfflinePermit.SigningUnavailable</c>. No case writes evidence.
    /// </summary>
    [Theory]
    [InlineData("refresh", "Auth.SessionInvalidated")]
    [InlineData("lease", "OfflinePermit.DeadlineReached")]
    [InlineData("licence", "Licence.VerificationUnavailable")]
    [InlineData("clock", "OfflinePermit.OutsideValidity")]
    [InlineData("none", "OfflinePermit.SigningUnavailable")]
    public async Task Unavailable_signer_at_reservation_reports_a_bound_passed_during_the_wait(string boundary, string expectedCode)
    {
        var verified = At("2026-11-20T12:00:00Z");
        var bound = boundary switch
        {
            "licence" => verified.AddHours(48),
            "refresh" => verified.AddHours(3),
            _ => verified.AddHours(2),
        };
        var reservedAt = bound.AddSeconds(-1);
        var afterWait = boundary switch
        {
            "clock" => reservedAt.AddSeconds(-1),
            "none" => reservedAt.AddMilliseconds(500),
            _ => bound,
        };
        using var host = CreateUnavailableSignerHost(() => true, () => _clock.Now = afterWait);
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenShopAsync(client, verified, verified.AddDays(30));
        if (boundary == "refresh")
            await PinAuthEndAsync(actor.Session, bound.AddDays(10), bound);

        _clock.Now = reservedAt;
        var stillAuthorized = boundary == "none";
        var (leaseId, capture) = await ReserveAsync(host, actor, leaseExpiresAt: boundary == "lease" ? bound : null,
            commitLegacyOnRefusal: stillAuthorized);

        Assert.Equal(expectedCode, Code(capture));
        if (boundary == "lease")
            Assert.Equal(OfflineLicencePermitPolicy.LeaseExpiry, capture.FirstError.Metadata?["limitingReason"]);
        Assert.Equal(afterWait, _clock.Now);
        Assert.Equal(new Evidence(0, 0, 0), await EvidenceAsync(actor.Shop));

        // Only the still-authorized signing failure leaves the reservation free to keep a legacy lease.
        await using var db = Open(Migrator);
        Assert.Equal(stillAuthorized, await db.InvoiceLeases.AsNoTracking().AnyAsync(x => x.Id == leaseId && x.ShopId == actor.Shop, Ct));
        _output.WriteLine("{0}: signer unavailable, clock {1:O} -> {2:O}, capture answers {3}; legacy lease kept {4}",
            boundary, reservedAt, afterWait, Code(capture), stillAuthorized);
    }

    /// <summary>
    /// The same precedence when the permit itself is being signed: a deadline that passed while the
    /// signer waited is reported as that deadline; a signer that fails inside the window stays
    /// <c>OfflinePermit.SigningUnavailable</c>. The grant and its key stay, no permit is written.
    /// </summary>
    [Theory]
    [InlineData("lease", "OfflinePermit.DeadlineReached")]
    [InlineData("licence", "Licence.VerificationUnavailable")]
    [InlineData("clock", "OfflinePermit.OutsideValidity")]
    [InlineData("none", "OfflinePermit.SigningUnavailable")]
    public async Task Unavailable_signer_at_issuance_reports_a_bound_passed_during_the_wait(string boundary, string expectedCode)
    {
        var verified = At("2026-11-20T12:00:00Z");
        var bound = boundary == "licence" ? verified.AddHours(48) : verified.AddHours(2);
        var issuedAt = bound.AddSeconds(-1);
        var afterWait = boundary switch
        {
            "clock" => issuedAt.AddMicroseconds(-1),
            "none" => issuedAt.AddMilliseconds(500),
            _ => bound,
        };
        var unavailable = false;
        using var host = CreateUnavailableSignerHost(() => unavailable, () => _clock.Now = afterWait);
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenShopAsync(client, verified, verified.AddDays(30));

        _clock.Now = issuedAt.AddMinutes(-1);
        var (lease, captured) = await ReserveAsync(host, actor, leaseExpiresAt: boundary == "lease" ? bound : null);
        var grant = Issued(captured).GrantId;
        _clock.Now = issuedAt;
        var done = await SnapshotAsync(host, actor, grant, lease);
        var before = await EvidenceAsync(actor.Shop);
        Assert.Equal(new Evidence(1, 0, 1), before);

        unavailable = true;
        var refused = await IssueAsync(host, actor, grant, done);

        Assert.Equal(expectedCode, Code(refused));
        if (boundary == "lease")
            Assert.Equal(OfflineLicencePermitPolicy.LeaseExpiry, refused.FirstError.Metadata?["limitingReason"]);
        Assert.Equal(afterWait, _clock.Now);
        Assert.Equal(before, await EvidenceAsync(actor.Shop));
    }

    /// <summary>The registered signer until <paramref name="unavailable"/>; then it waits and returns nothing.</summary>
    private ApiWebApplicationFactory CreateUnavailableSignerHost(Func<bool> unavailable, Action whileSigning) =>
        CreateHost(configure: services =>
        {
            var inner = services.Last(x => x.ServiceType == typeof(IOfflinePermitSigner));
            services.AddSingleton<IOfflinePermitSigner>(provider => new UnavailableSigner(
                (IOfflinePermitSigner)inner.ImplementationFactory!(provider), unavailable, whileSigning));
        });

    /// <summary>A remote key that times out: the wait happens, no signature comes back. Cancellation still throws.</summary>
    private sealed class UnavailableSigner(IOfflinePermitSigner inner, Func<bool> unavailable, Action whileSigning)
        : IOfflinePermitSigner
    {
        public async Task<OfflinePermitSignature?> SignAsync(string type, byte[] payload, CancellationToken ct)
        {
            if (!unavailable())
                return await inner.SignAsync(type, payload, ct);

            ct.ThrowIfCancellationRequested();
            whileSigning();
            return null;
        }
    }
}
