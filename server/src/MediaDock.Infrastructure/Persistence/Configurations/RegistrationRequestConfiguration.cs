using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaDock.Infrastructure.Persistence.Configurations;

internal sealed class RegistrationRequestConfiguration : IEntityTypeConfiguration<RegistrationRequest>
{
    public void Configure(EntityTypeBuilder<RegistrationRequest> builder)
    {
        builder.ToTable("registration_requests", table =>
        {
            table.HasCheckConstraint(
                "ck_registration_requests_status",
                "status IN ('pending', 'approved', 'rejected')");
            table.HasCheckConstraint(
                "ck_registration_requests_decision",
                "(status = 'pending' AND decided_at IS NULL AND decided_by IS NULL) OR " +
                "(status IN ('approved', 'rejected') AND decided_at IS NOT NULL AND decided_by IS NOT NULL AND btrim(decided_by) <> '')");
        });
        builder.HasKey(entity => entity.Id).HasName("pk_registration_requests");
        builder.Property(entity => entity.Id).HasColumnName("id");
        builder.Property(entity => entity.UserId).HasColumnName("user_id");
        builder.Property(entity => entity.RequestedAt).HasColumnName("requested_at");
        builder.Property(entity => entity.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.DecidedAt).HasColumnName("decided_at");
        builder.Property(entity => entity.DecidedBy).HasColumnName("decided_by").HasMaxLength(255);
        builder.HasIndex(entity => new { entity.UserId, entity.RequestedAt })
            .HasDatabaseName("ix_registration_requests_user_id_requested_at");
        builder.HasIndex(entity => entity.UserId)
            .IsUnique()
            .HasDatabaseName("ux_registration_requests_pending_user_id")
            .HasFilter("status = 'pending'");
        builder.HasOne(entity => entity.User)
            .WithMany(user => user.RegistrationRequests)
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_registration_requests_users_user_id");
    }
}