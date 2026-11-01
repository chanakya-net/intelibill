using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class OperationReceiptConfiguration : IEntityTypeConfiguration<OperationReceipt>
{
    public void Configure(EntityTypeBuilder<OperationReceipt> builder)
    {
        builder.ToTable("operation_receipts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OperationKey).HasMaxLength(OperationReceipt.MaxKeyLength).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ResponseBody).HasColumnType("jsonb");

        // The reservation's ON CONFLICT target. Independent of the actor on purpose.
        builder.HasIndex(x => new { x.ShopId, x.OperationType, x.OperationKey }).IsUnique();
    }
}
