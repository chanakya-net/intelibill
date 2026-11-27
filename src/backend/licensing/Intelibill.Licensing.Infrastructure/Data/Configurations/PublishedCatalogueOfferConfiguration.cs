using Intelibill.Licensing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Licensing.Infrastructure.Data.Configurations;

internal sealed class PublishedCatalogueOfferConfiguration : IEntityTypeConfiguration<PublishedCatalogueOffer>
{
    public void Configure(EntityTypeBuilder<PublishedCatalogueOffer> builder)
    {
        builder.ToTable("published_catalogue_offers", table => table.HasCheckConstraint("ck_published_catalogue_offer",
            "version_number > 0 AND octet_length(snapshot) BETWEEN 2 AND 2400"));
        builder.HasKey(offer => offer.VersionId);
        builder.Property(offer => offer.VersionId).ValueGeneratedNever();
        builder.HasIndex(offer => new { offer.DevelopmentFixture, offer.PlanId, offer.VersionNumber }).IsUnique();
        builder.HasOne<CataloguePublication>().WithOne().HasForeignKey<PublishedCatalogueOffer>(offer => offer.VersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
