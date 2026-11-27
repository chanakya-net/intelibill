using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.Shops.Setup;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Infrastructure.Repositories;

namespace Intelibill.Integration.Tests;

internal static class ExpirySettingsFixture
{
    public static ShopSetupAccess Access(ApplicationDbContext db)
    {
        var work = new UnitOfWork(db);
        return new(work, new ShopRepository(db), new MembershipAuthorityRepository(db, work),
            new FixtureAuthority(), new TenantWriteScope(db), new ShopLicenceFence(db, new LicenceActionPolicy(), TimeProvider.System));
    }

    public static SessionClaims Session(Guid actor, Guid shop) => new(actor, Guid.NewGuid(), 0, 0, shop, "Owner");

    // These fixtures seed users directly and exercise expiry storage without running an HTTP auth host.
    private sealed class FixtureAuthority : ILiveSessionAuthority
    {
        public Task<bool> IsValidAsync(SessionClaims claims, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
