using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaDock.Infrastructure.Persistence.Configurations;

internal sealed class UserIgnoredMovieConfiguration : IEntityTypeConfiguration<UserIgnoredMovie>
{
    public void Configure(EntityTypeBuilder<UserIgnoredMovie> builder)
    {
        builder.ToTable("user_ignored_movies");
        builder.HasKey(entity => new { entity.UserId, entity.ImdbId }).HasName("pk_user_ignored_movies");
        builder.Property(entity => entity.UserId).HasColumnName("user_id");
        builder.Property(entity => entity.ImdbId).HasColumnName("imdb_id").HasMaxLength(14).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(entity => new { entity.UserId, entity.CreatedAt })
            .HasDatabaseName("ix_user_ignored_movies_user_id_created_at");
        builder.HasOne(entity => entity.ImdbTitle)
            .WithMany()
            .HasForeignKey(entity => entity.ImdbId)
            .HasPrincipalKey(title => title.ImdbId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired()
            .HasConstraintName("fk_user_ignored_movies_imdb_titles_imdb_id");
        builder.HasOne(entity => entity.User)
            .WithMany()
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_user_ignored_movies_users_user_id");
    }
}