using Intelibill.Licensing.Infrastructure.AdminIdentities;
using Intelibill.Licensing.Infrastructure.Bootstrap;
using Intelibill.Licensing.Infrastructure.AdminIdentities.Mfa;
using Intelibill.Licensing.Infrastructure.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Data;

public sealed class LicensingDbContext(DbContextOptions<LicensingDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new BootstrapMapping());
        modelBuilder.ApplyConfiguration(new AdminIdentityMapping());
        modelBuilder.ApplyConfiguration(new EnrollmentCredentialMapping());
        modelBuilder.ApplyConfiguration(new ProviderAuditMapping());
        modelBuilder.ApplyConfiguration(new BootstrapRateBucketMapping());
        modelBuilder.ApplyConfiguration(new AdminInvitationMapping());
        modelBuilder.ApplyConfiguration(new AdminSessionMapping());
        modelBuilder.ApplyConfiguration(new AdminRefreshCredentialMapping());
        modelBuilder.ApplyConfiguration(new AdminSignInBucketMapping());
        modelBuilder.ApplyConfiguration(new AdminTotpMapping());
        modelBuilder.ApplyConfiguration(new AdminRecoveryCodeMapping());
        modelBuilder.ApplyConfiguration(new AdminRecoveryMapping());
        modelBuilder.ApplyConfiguration(new MerchantBillingDelegationUseConfiguration());
        modelBuilder.ApplyConfiguration(new BillingAccountConfiguration());
        modelBuilder.ApplyConfiguration(new BillingAccountMemberConfiguration());
        modelBuilder.ApplyConfiguration(new PlanConfiguration());
        modelBuilder.ApplyConfiguration(new PlanVersionConfiguration());
        modelBuilder.ApplyConfiguration(new TrialPolicyVersionConfiguration());
        modelBuilder.ApplyConfiguration(new CatalogueDraftChangeConfiguration());
    }
}
