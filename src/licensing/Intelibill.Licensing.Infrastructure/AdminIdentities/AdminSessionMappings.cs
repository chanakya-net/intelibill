using Intelibill.Licensing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities;

internal sealed class AdminSessionMapping : IEntityTypeConfiguration<AdminSession>
{
    public void Configure(EntityTypeBuilder<AdminSession> builder)
    {
        builder.ToTable("admin_sessions", table =>
            table.HasCheckConstraint("ck_admin_session_dates",
                "mfa_verified_at >= created_at AND last_seen_at >= created_at AND (revoked_at IS NULL OR revoked_at >= created_at)"));
        builder.HasOne<AdminIdentity>().WithMany().HasForeignKey(session => session.AdminId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AdminSignInBucket
{
    public string Subject { get; init; } = null!;
    public DateTimeOffset WindowEndsAt { get; init; }
    public int Attempts { get; init; }
}

internal sealed class AdminSignInBucketMapping : IEntityTypeConfiguration<AdminSignInBucket>
{
    public void Configure(EntityTypeBuilder<AdminSignInBucket> builder)
    {
        builder.ToTable("admin_sign_in_buckets", table =>
            table.HasCheckConstraint("ck_admin_sign_in_attempts", "attempts > 0"));
        builder.HasKey(bucket => bucket.Subject);
        builder.Property(bucket => bucket.Subject).HasMaxLength(64);
        builder.HasIndex(bucket => bucket.WindowEndsAt);
    }
}
