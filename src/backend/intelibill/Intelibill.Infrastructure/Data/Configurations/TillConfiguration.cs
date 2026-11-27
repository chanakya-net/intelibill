using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class TillConfiguration : IEntityTypeConfiguration<Till>
{
    public void Configure(EntityTypeBuilder<Till> b)
    {
        b.ToTable("tills", t => t.HasCheckConstraint("ck_tills_name", "length(btrim(name)) BETWEEN 1 AND 100 AND length(btrim(normalized_name)) BETWEEN 1 AND 100"));
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.Id, x.ShopId });
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.NormalizedName).HasMaxLength(100);
        b.HasIndex(x => new { x.ShopId, x.NormalizedName }).IsUnique();
        b.HasOne<Shop>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ShopMembership>().WithMany().HasPrincipalKey(x => new { x.ShopId, x.UserId })
            .HasForeignKey(x => new { x.ShopId, UserId = x.CreatedBy }).OnDelete(DeleteBehavior.Restrict);
    }
}
