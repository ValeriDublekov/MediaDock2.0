using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaDock.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", table =>
        {
            table.HasCheckConstraint(
                "ck_users_normalized_email",
                "normalized_email <> '' AND normalized_email = lower(btrim(normalized_email))");
            table.HasCheckConstraint("ck_users_given_name", "btrim(given_name) <> ''");
            table.HasCheckConstraint("ck_users_family_name", "btrim(family_name) <> ''");
            table.HasCheckConstraint("ck_users_status", "status IN ('pending', 'active', 'deactivated')");
            table.HasCheckConstraint("ck_users_role", "role IS NULL OR role IN ('admin', 'user')");
            table.HasCheckConstraint(
                "ck_users_status_role",
                "(status = 'pending' AND role IS NULL) OR (status IN ('active', 'deactivated') AND role IS NOT NULL)");
        });
        builder.HasKey(entity => entity.Id).HasName("pk_users");
        builder.Property(entity => entity.Id).HasColumnName("id");
        builder.Property(entity => entity.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(320).IsRequired();
        builder.Property(entity => entity.GivenName).HasColumnName("given_name").HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.FamilyName).HasColumnName("family_name").HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.Role).HasColumnName("role").HasMaxLength(16);
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(entity => entity.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("ux_users_normalized_email");
    }
}