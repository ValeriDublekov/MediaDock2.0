using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaDock.Infrastructure.Persistence.Configurations;

internal sealed class PersonalRatingConfiguration : IEntityTypeConfiguration<PersonalRating>
{
    public void Configure(EntityTypeBuilder<PersonalRating> builder)
    {
        builder.ToTable("personal_ratings", table =>
            table.HasCheckConstraint("ck_personal_ratings_rating", "rating BETWEEN 1 AND 10"));
        builder.HasKey(entity => new { entity.UserId, entity.ImdbId }).HasName("pk_personal_ratings");
        builder.Property(entity => entity.UserId).HasColumnName("user_id");
        builder.Property(entity => entity.ImdbId).HasColumnName("imdb_id").HasMaxLength(14);
        builder.Property(entity => entity.Rating).HasColumnName("rating");
        builder.Property(entity => entity.IsManuallyAdded).HasColumnName("is_manually_added").HasDefaultValue(false);
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        builder.HasOne(entity => entity.ImdbTitle)
            .WithMany()
            .HasForeignKey(entity => entity.ImdbId)
            .HasPrincipalKey(imdbTitle => imdbTitle.ImdbId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired()
            .HasConstraintName("fk_personal_ratings_imdb_titles_imdb_id");
        builder.HasOne(entity => entity.User)
            .WithMany()
            .HasForeignKey(entity => entity.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_personal_ratings_users_user_id");
    }
}