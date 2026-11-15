using Intelibill.Licensing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Licensing.Infrastructure.Data.Configurations;

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans", table =>
        {
            table.HasCheckConstraint("ck_plans_kind", "kind IN (1, 2, 3)");
            table.HasCheckConstraint("ck_plans_id", "id <> '00000000-0000-0000-0000-000000000000'");
        });
        builder.HasKey(plan => plan.Id);
        builder.Property(plan => plan.Id).ValueGeneratedNever();
        builder.HasOne<AdminIdentity>().WithMany().HasForeignKey(plan => plan.CreatedByAdminId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
