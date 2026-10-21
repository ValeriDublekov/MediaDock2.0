using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaDock.Infrastructure.Persistence.Configurations;

internal sealed class FavoriteMovieConfiguration : IEntityTypeConfiguration<FavoriteMovie>
{
    public void Configure(EntityTypeBuilder<FavoriteMovie> builder)
    {
        builder.ToTable("legacy_favorite_movies", table =>
            table.HasCheckConstraint("ck_favorite_movies_origin", "added_from_oscar OR added_from_catalog"));
        builder.HasKey(entity => new { entity.UserId, entity.TitleId }).HasName("pk_favorite_movies");
        builder.Property(entity => entity.UserId).HasColumnName("user_id");
        builder.Property(entity => entity.TitleId).HasColumnName("title_id");
        builder.Property(entity => entity.ToWatch).HasColumnName("to_watch");
        builder.Property(entity => entity.ToDownload).HasColumnName("to_download");
        builder.Property(entity => entity.AddedFromOscar).HasColumnName("added_from_oscar");
        builder.Property(entity => entity.AddedFromCatalog).HasColumnName("added_from_catalog");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        builder.HasOne(entity => entity.Title)
            .WithMany()
            .HasForeignKey(entity => entity.TitleId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_favorite_movies_titles_title_id");
        builder.HasOne(entity => entity.User)
            .WithMany()
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_favorite_movies_users_user_id");
    }
}

internal sealed class ImdbFavoriteMovieConfiguration : IEntityTypeConfiguration<ImdbFavoriteMovie>
{
    public void Configure(EntityTypeBuilder<ImdbFavoriteMovie> builder)
    {
        builder.ToTable("favorite_movies", table =>
            table.HasCheckConstraint("ck_favorite_movies_origin", "added_from_oscar OR added_from_catalog"));
        builder.HasKey(entity => new { entity.UserId, entity.ImdbId }).HasName("pk_favorite_movies_imdb");
        builder.Property(entity => entity.UserId).HasColumnName("user_id");
        builder.Property(entity => entity.ImdbId).HasColumnName("imdb_id").HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.ToWatch).HasColumnName("to_watch");
        builder.Property(entity => entity.ToDownload).HasColumnName("to_download");
        builder.Property(entity => entity.AddedFromOscar).HasColumnName("added_from_oscar");
        builder.Property(entity => entity.AddedFromCatalog).HasColumnName("added_from_catalog");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        builder.HasOne(entity => entity.ImdbTitle)
            .WithMany()
            .HasForeignKey(entity => entity.ImdbId)
            .HasPrincipalKey(imdbTitle => imdbTitle.ImdbId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_favorite_movies_imdb_titles_imdb_id");
        builder.HasOne(entity => entity.User)
            .WithMany()
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_favorite_movies_users_user_id");
    }
}