using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceTitleMetadataOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE imdb_titles AS canonical
                SET imdb_rating = COALESCE(canonical.imdb_rating, title.imdb_rating),
                    imdb_votes = COALESCE(canonical.imdb_votes, title.imdb_votes),
                    metascore = COALESCE(canonical.metascore, title.metascore),
                    genres = CASE WHEN cardinality(canonical.genres) = 0 THEN title.genres ELSE canonical.genres END,
                    countries = CASE WHEN cardinality(canonical.countries) = 0 THEN title.countries ELSE canonical.countries END,
                    director = COALESCE(NULLIF(btrim(canonical.director), ''), title.director),
                    plot = COALESCE(NULLIF(btrim(canonical.plot), ''), title.plot),
                    poster_url = COALESCE(NULLIF(btrim(canonical.poster_url), ''), title.poster_url),
                    runtime = COALESCE(NULLIF(btrim(canonical.runtime), ''), title.runtime),
                    awards = COALESCE(NULLIF(btrim(canonical.awards), ''), title.awards),
                    box_office = COALESCE(NULLIF(btrim(canonical.box_office), ''), title.box_office),
                    updated_at = GREATEST(canonical.updated_at, title.updated_at)
                FROM titles AS title
                WHERE title.imdb_id = canonical.imdb_id;

                                UPDATE imdb_titles
                                SET metadata_status = 'enriched'
                                WHERE metadata_status = 'pending'
                                    AND (imdb_rating IS NOT NULL
                                        OR imdb_votes IS NOT NULL
                                        OR metascore IS NOT NULL
                                        OR cardinality(genres) > 0
                                        OR cardinality(countries) > 0
                                        OR NULLIF(btrim(director), '') IS NOT NULL
                                        OR NULLIF(btrim(plot), '') IS NOT NULL
                                        OR NULLIF(btrim(poster_url), '') IS NOT NULL
                                        OR NULLIF(btrim(runtime), '') IS NOT NULL
                                        OR NULLIF(btrim(awards), '') IS NOT NULL
                                        OR NULLIF(btrim(box_office), '') IS NOT NULL);

                UPDATE titles
                SET imdb_rating = NULL,
                    imdb_votes = NULL,
                    metascore = NULL,
                    genres = ARRAY[]::text[],
                    countries = ARRAY[]::text[],
                    director = NULL,
                    plot = NULL,
                    poster_url = NULL,
                    runtime = NULL,
                    awards = NULL,
                    box_office = NULL
                WHERE imdb_id IS NOT NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_titles_provider_metadata_without_imdb_id",
                table: "titles",
                sql: "imdb_id IS NULL OR (imdb_rating IS NULL AND imdb_votes IS NULL AND metascore IS NULL AND cardinality(genres) = 0 AND cardinality(countries) = 0 AND NULLIF(btrim(director), '') IS NULL AND NULLIF(btrim(plot), '') IS NULL AND NULLIF(btrim(poster_url), '') IS NULL AND NULLIF(btrim(runtime), '') IS NULL AND NULLIF(btrim(awards), '') IS NULL AND NULLIF(btrim(box_office), '') IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_titles_provider_metadata_without_imdb_id",
                table: "titles");

            migrationBuilder.Sql(
                """
                UPDATE titles AS title
                SET imdb_rating = canonical.imdb_rating,
                    imdb_votes = canonical.imdb_votes,
                    metascore = canonical.metascore,
                    genres = canonical.genres,
                    countries = canonical.countries,
                    director = canonical.director,
                    plot = canonical.plot,
                    poster_url = canonical.poster_url,
                    runtime = canonical.runtime,
                    awards = canonical.awards,
                    box_office = canonical.box_office
                FROM imdb_titles AS canonical
                WHERE title.imdb_id = canonical.imdb_id;
                """);
        }
    }
}
