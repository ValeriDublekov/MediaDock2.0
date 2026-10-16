using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedImdbTitles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "imdb_titles",
                columns: table => new
                {
                    imdb_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "text", nullable: true),
                    year = table.Column<int>(type: "integer", nullable: true),
                    media_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    imdb_rating = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    imdb_votes = table.Column<long>(type: "bigint", nullable: true),
                    metascore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    genres = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "ARRAY[]::text[]"),
                    countries = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "ARRAY[]::text[]"),
                    director = table.Column<string>(type: "text", nullable: true),
                    plot = table.Column<string>(type: "text", nullable: true),
                    poster_url = table.Column<string>(type: "text", nullable: true),
                    runtime = table.Column<string>(type: "text", nullable: true),
                    awards = table.Column<string>(type: "text", nullable: true),
                    box_office = table.Column<string>(type: "text", nullable: true),
                    metadata_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false, defaultValue: "pending"),
                    enrichment_attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_enrichment_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_enrichment_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_enrichment_error = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_imdb_titles", x => x.imdb_id);
                    table.CheckConstraint("ck_imdb_titles_enrichment_attempt_count", "enrichment_attempt_count >= 0");
                    table.CheckConstraint("ck_imdb_titles_imdb_id_normalized", "imdb_id <> '' AND imdb_id = lower(btrim(imdb_id))");
                    table.CheckConstraint("ck_imdb_titles_metadata_status", "metadata_status IN ('pending', 'enriched', 'not_found', 'temporary_error')");
                });

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM personal_ratings WHERE btrim(imdb_id) = '') THEN
                        RAISE EXCEPTION 'Cannot migrate blank IMDb IDs in personal_ratings';
                    END IF;
                END $$;

                INSERT INTO imdb_titles (imdb_id, metadata_status, enrichment_attempt_count, updated_at)
                SELECT known_ids.imdb_id, 'pending', 0, now()
                FROM (
                    SELECT lower(btrim(imdb_id)) AS imdb_id FROM titles WHERE imdb_id IS NOT NULL AND btrim(imdb_id) <> ''
                    UNION
                    SELECT lower(btrim(imdb_id)) FROM personal_ratings WHERE btrim(imdb_id) <> ''
                    UNION
                    SELECT lower(btrim(imdb_id)) FROM oscar_films WHERE imdb_id IS NOT NULL AND btrim(imdb_id) <> ''
                    UNION
                    SELECT lower(btrim(imdb_id)) FROM golden_globe_nominations WHERE imdb_id IS NOT NULL AND btrim(imdb_id) <> ''
                ) AS known_ids
                ON CONFLICT (imdb_id) DO NOTHING;

                WITH title_metadata AS (
                    SELECT DISTINCT ON (lower(btrim(imdb_id)))
                           lower(btrim(imdb_id)) AS imdb_id,
                           title,
                           year,
                           media_type,
                           imdb_rating,
                           imdb_votes,
                           metascore,
                           genres,
                           countries,
                           director,
                           plot,
                           poster_url,
                           runtime,
                           awards,
                           box_office,
                           updated_at
                    FROM titles
                    WHERE imdb_id IS NOT NULL AND btrim(imdb_id) <> ''
                    ORDER BY lower(btrim(imdb_id)), updated_at DESC, id
                )
                UPDATE imdb_titles AS target
                SET title = source.title,
                    year = source.year,
                    media_type = source.media_type,
                    imdb_rating = source.imdb_rating,
                    imdb_votes = source.imdb_votes,
                    metascore = source.metascore,
                    genres = source.genres,
                    countries = source.countries,
                    director = source.director,
                    plot = source.plot,
                    poster_url = source.poster_url,
                    runtime = source.runtime,
                    awards = source.awards,
                    box_office = source.box_office,
                    metadata_status = CASE
                        WHEN source.imdb_rating IS NOT NULL
                          OR source.imdb_votes IS NOT NULL
                          OR source.metascore IS NOT NULL
                          OR cardinality(source.genres) > 0
                          OR cardinality(source.countries) > 0
                          OR NULLIF(btrim(source.director), '') IS NOT NULL
                          OR NULLIF(btrim(source.plot), '') IS NOT NULL
                          OR NULLIF(btrim(source.poster_url), '') IS NOT NULL
                          OR NULLIF(btrim(source.runtime), '') IS NOT NULL
                          OR NULLIF(btrim(source.awards), '') IS NOT NULL
                          OR NULLIF(btrim(source.box_office), '') IS NOT NULL
                        THEN 'enriched'
                        ELSE 'pending'
                    END,
                    updated_at = source.updated_at
                FROM title_metadata AS source
                WHERE target.imdb_id = source.imdb_id;

                WITH ranked_titles AS (
                    SELECT id,
                           lower(btrim(imdb_id)) AS normalized_id,
                           row_number() OVER (PARTITION BY lower(btrim(imdb_id)) ORDER BY id) AS duplicate_rank
                    FROM titles
                    WHERE imdb_id IS NOT NULL AND btrim(imdb_id) <> ''
                )
                UPDATE titles AS target
                SET imdb_id = NULL
                FROM ranked_titles AS ranked
                WHERE target.id = ranked.id AND ranked.duplicate_rank > 1;

                UPDATE titles
                SET imdb_id = lower(btrim(imdb_id))
                WHERE imdb_id IS NOT NULL AND btrim(imdb_id) <> '';

                UPDATE titles SET imdb_id = NULL WHERE imdb_id IS NOT NULL AND btrim(imdb_id) = '';

                WITH ranked_ratings AS (
                    SELECT ctid,
                           row_number() OVER (
                               PARTITION BY user_id, lower(btrim(imdb_id))
                               ORDER BY updated_at DESC, imdb_id
                           ) AS duplicate_rank
                    FROM personal_ratings
                )
                DELETE FROM personal_ratings AS target
                USING ranked_ratings AS ranked
                WHERE target.ctid = ranked.ctid AND ranked.duplicate_rank > 1;

                UPDATE personal_ratings SET imdb_id = lower(btrim(imdb_id));
                UPDATE oscar_films SET imdb_id = NULLIF(lower(btrim(imdb_id)), '') WHERE imdb_id IS NOT NULL;
                UPDATE golden_globe_nominations SET imdb_id = NULLIF(lower(btrim(imdb_id)), '') WHERE imdb_id IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_personal_ratings_imdb_id",
                table: "personal_ratings",
                column: "imdb_id");

            migrationBuilder.CreateIndex(
                name: "IX_oscar_films_imdb_id",
                table: "oscar_films",
                column: "imdb_id");

            migrationBuilder.CreateIndex(
                name: "IX_golden_globe_nominations_imdb_id",
                table: "golden_globe_nominations",
                column: "imdb_id");

            migrationBuilder.AddForeignKey(
                name: "fk_golden_globe_nominations_imdb_titles_imdb_id",
                table: "golden_globe_nominations",
                column: "imdb_id",
                principalTable: "imdb_titles",
                principalColumn: "imdb_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_oscar_films_imdb_titles_imdb_id",
                table: "oscar_films",
                column: "imdb_id",
                principalTable: "imdb_titles",
                principalColumn: "imdb_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_personal_ratings_imdb_titles_imdb_id",
                table: "personal_ratings",
                column: "imdb_id",
                principalTable: "imdb_titles",
                principalColumn: "imdb_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_titles_imdb_titles_imdb_id",
                table: "titles",
                column: "imdb_id",
                principalTable: "imdb_titles",
                principalColumn: "imdb_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_golden_globe_nominations_imdb_titles_imdb_id",
                table: "golden_globe_nominations");

            migrationBuilder.DropForeignKey(
                name: "fk_oscar_films_imdb_titles_imdb_id",
                table: "oscar_films");

            migrationBuilder.DropForeignKey(
                name: "fk_personal_ratings_imdb_titles_imdb_id",
                table: "personal_ratings");

            migrationBuilder.DropForeignKey(
                name: "fk_titles_imdb_titles_imdb_id",
                table: "titles");

            migrationBuilder.DropTable(
                name: "imdb_titles");

            migrationBuilder.DropIndex(
                name: "IX_personal_ratings_imdb_id",
                table: "personal_ratings");

            migrationBuilder.DropIndex(
                name: "IX_oscar_films_imdb_id",
                table: "oscar_films");

            migrationBuilder.DropIndex(
                name: "IX_golden_globe_nominations_imdb_id",
                table: "golden_globe_nominations");
        }
    }
}
