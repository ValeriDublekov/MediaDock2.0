using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaDock.Infrastructure.Persistence.Configurations;

internal sealed class FavoriteMovieConfiguration : IEntityTypeConfiguration<FavoriteMovie>
{
    public void Configure(EntityTypeBuilder<FavoriteMovie> builder)
    {
        builder.ToTable("favorite_movies", table =>
            table.HasCheckConstraint("ck_favorite_movies_origin", "added_from_oscar OR added_from_catalog"));
        builder.HasKey(entity => entity.TitleId).HasName("pk_favorite_movies");
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
    }
}