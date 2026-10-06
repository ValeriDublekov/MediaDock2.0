using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaDock.Infrastructure.Persistence.Configurations;

internal sealed class ExternalIdentityConfiguration : IEntityTypeConfiguration<ExternalIdentity>
{
    public void Configure(EntityTypeBuilder<ExternalIdentity> builder)
    {
        builder.ToTable("external_identities", table =>
            table.HasCheckConstraint(
                "ck_external_identities_issuer_subject",
                "btrim(issuer) <> '' AND btrim(subject) <> ''"));
        builder.HasKey(entity => entity.Id).HasName("pk_external_identities");
        builder.Property(entity => entity.Id).HasColumnName("id");
        builder.Property(entity => entity.UserId).HasColumnName("user_id");
        builder.Property(entity => entity.Issuer).HasColumnName("issuer").HasMaxLength(512).IsRequired();
        builder.Property(entity => entity.Subject).HasColumnName("subject").HasMaxLength(512).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(entity => new { entity.Issuer, entity.Subject })
            .IsUnique()
            .HasDatabaseName("ux_external_identities_issuer_subject");
        builder.HasIndex(entity => entity.UserId).HasDatabaseName("ix_external_identities_user_id");
        builder.HasOne(entity => entity.User)
            .WithMany(user => user.ExternalIdentities)
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_external_identities_users_user_id");
    }
}