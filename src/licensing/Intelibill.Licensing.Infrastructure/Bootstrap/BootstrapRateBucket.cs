using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Licensing.Infrastructure.Bootstrap;

internal sealed class BootstrapRateBucket
{
    public int Id { get; private set; }
    public DateTimeOffset WindowEndsAt { get; private set; }
    public int Attempts { get; private set; }
}

internal sealed class BootstrapRateBucketMapping : IEntityTypeConfiguration<BootstrapRateBucket>
{
    public void Configure(EntityTypeBuilder<BootstrapRateBucket> builder)
    {
        builder.ToTable("bootstrap_rate_buckets", table =>
            table.HasCheckConstraint("ck_bootstrap_rate_bucket", "id BETWEEN 1 AND 3 AND attempts >= 0"));
        builder.Property(bucket => bucket.Id).ValueGeneratedNever();
    }
}
