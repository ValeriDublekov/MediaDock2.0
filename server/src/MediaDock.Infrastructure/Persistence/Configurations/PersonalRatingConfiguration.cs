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
        builder.HasKey(entity => entity.ImdbId).HasName("pk_personal_ratings");
        builder.Property(entity => entity.ImdbId).HasColumnName("imdb_id").HasMaxLength(14);
        builder.Property(entity => entity.Rating).HasColumnName("rating");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
    }
}