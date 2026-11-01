using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelibill.Infrastructure.Data.Configurations;

internal sealed class RealtimeConnectionLeaseConfiguration : IEntityTypeConfiguration<RealtimeConnectionLease>
{
    public void Configure(EntityTypeBuilder<RealtimeConnectionLease> builder)
    {
        builder.ToTable("realtime_connection_leases");

        builder.HasKey(lease => lease.Id);

        builder.Property(lease => lease.ConnectionId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(lease => lease.InstanceId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(lease => lease.HubName)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(lease => lease.UserId).IsRequired();
        builder.Property(lease => lease.SessionId).IsRequired();
        builder.Property(lease => lease.ShopId).IsRequired();

        builder.Property(lease => lease.ShopRole)
            .HasMaxLength(32);

        builder.Property(lease => lease.SecurityVersion).IsRequired();
        builder.Property(lease => lease.ScopeVersion).IsRequired();
        builder.Property(lease => lease.ConnectedAt).IsRequired();
        builder.Property(lease => lease.ExpiresAt).IsRequired();

        // A SignalR connection id identifies exactly one transport, so a second row for the same id
        // would make "who may receive this" ambiguous.
        builder.HasIndex(lease => lease.ConnectionId).IsUnique();

        // Recipient resolution reads by hub and shop; the sweep reads by instance and by expiry.
        builder.HasIndex(lease => new { lease.HubName, lease.ShopId });
        builder.HasIndex(lease => lease.InstanceId);
        builder.HasIndex(lease => lease.ExpiresAt);

        // No navigation to User, Shop or AuthSession and no cascade: a lease is a transient record
        // of a socket. Deleting a shop or ending a session must not wait on, or be blocked by, rows
        // that the sweep removes on its own within the token's lifetime.
    }
}
