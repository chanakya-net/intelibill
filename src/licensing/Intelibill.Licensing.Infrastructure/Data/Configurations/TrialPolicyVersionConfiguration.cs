using Intelibill.Licensing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Licensing.Infrastructure.Data.Configurations;

internal sealed class TrialPolicyVersionConfiguration : IEntityTypeConfiguration<TrialPolicyVersion>
{
    public void Configure(EntityTypeBuilder<TrialPolicyVersion> builder)
    {
        builder.ToTable("trial_policy_versions", table =>
        {
            table.HasCheckConstraint("ck_trial_policy_versions_duration", "duration_days = 14");
            table.HasCheckConstraint("ck_trial_policy_versions_caps",
                "active_member_cap BETWEEN 1 AND 100000 AND max_concurrent_trial_shops BETWEEN 1 AND 100000");
            table.HasCheckConstraint("ck_trial_policy_versions_status", "status IN (1, 2, 3)");
            table.HasCheckConstraint("ck_trial_policy_versions_numbers", "version_number >= 1 AND revision >= 1");
            table.HasCheckConstraint("ck_trial_policy_versions_terms",
                $"trial_terms_version ~ {PlanVersionConfiguration.PolicyVersionPattern}");
        });
        builder.HasKey(version => version.Id);
        builder.Property(version => version.Id).ValueGeneratedNever();
        builder.Property(version => version.TrialTermsVersion).HasMaxLength(40);
        builder.Ignore(version => version.Facts);
        builder.HasIndex(version => version.VersionNumber).IsUnique();
        // One open trial policy draft.
        builder.HasIndex(version => version.Status).IsUnique().HasFilter("status = 1")
            .HasDatabaseName("ux_trial_policy_versions_open_draft");
    }
}
