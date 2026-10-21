using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyTitleIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    fallback_metadata_title_count bigint;
                    legacy_favorite_count bigint;
                BEGIN
                    SELECT COUNT(*) INTO fallback_metadata_title_count
                    FROM titles AS title
                    WHERE title.imdb_rating IS NOT NULL
                       OR title.imdb_votes IS NOT NULL
                       OR title.metascore IS NOT NULL
                       OR COALESCE(cardinality(title.genres), 0) > 0
                       OR COALESCE(cardinality(title.countries), 0) > 0
                       OR NULLIF(btrim(title.director), '') IS NOT NULL
                       OR NULLIF(btrim(title.plot), '') IS NOT NULL
                       OR NULLIF(btrim(title.poster_url), '') IS NOT NULL
                       OR NULLIF(btrim(title.runtime), '') IS NOT NULL
                       OR NULLIF(btrim(title.awards), '') IS NOT NULL
                       OR NULLIF(btrim(title.box_office), '') IS NOT NULL;

                    SELECT COUNT(*) INTO legacy_favorite_count
                    FROM legacy_favorite_movies;

                    RAISE NOTICE 'Cleanup will discard provider fallback metadata for % legacy titles and % retained legacy favorite rows',
                        fallback_metadata_title_count, legacy_favorite_count;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_golden_globe_nominations_groups_group_id",
                table: "golden_globe_nominations");

            migrationBuilder.DropForeignKey(
                name: "fk_oscar_nominations_films_oscar_film_id",
                table: "oscar_nominations");

            migrationBuilder.DropTable(
                name: "golden_globe_groups");

            migrationBuilder.DropTable(
                name: "legacy_favorite_movies");

            migrationBuilder.DropTable(
                name: "occurrences");

            migrationBuilder.DropTable(
                name: "oscar_films");

            migrationBuilder.DropTable(
                name: "titles");

            migrationBuilder.DropIndex(
                name: "IX_oscar_nominations_oscar_film_id",
                table: "oscar_nominations");

            migrationBuilder.DropIndex(
                name: "ix_golden_globe_nominations_group_award",
                table: "golden_globe_nominations");

            migrationBuilder.DropColumn(
                name: "oscar_film_id",
                table: "oscar_nominations");

            migrationBuilder.DropColumn(
                name: "group_id",
                table: "golden_globe_nominations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "oscar_film_id",
                table: "oscar_nominations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "group_id",
                table: "golden_globe_nominations",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "golden_globe_groups",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    imdb_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    enrichment_attempt_count = table.Column<int>(type: "integer", nullable: false),
                    enrichment_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    imdb_id_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_imdb_id_manual = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    last_enrichment_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_enrichment_error = table.Column<string>(type: "text", nullable: true),
                    next_enrichment_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    nominee_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_golden_globe_groups", x => x.id);
                    table.CheckConstraint("ck_golden_globe_groups_enrichment_attempt_count", "enrichment_attempt_count >= 0");
                    table.CheckConstraint("ck_golden_globe_groups_enrichment_status", "enrichment_status IN ('pending', 'enriched', 'problem', 'not_found', 'temporary_error')");
                    table.CheckConstraint("ck_golden_globe_groups_nominee_type", "nominee_type IN ('movie', 'series')");
                    table.ForeignKey(
                        name: "fk_golden_globe_groups_imdb_titles_imdb_id",
                        column: x => x.imdb_id,
                        principalTable: "imdb_titles",
                        principalColumn: "imdb_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "titles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    imdb_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    awards = table.Column<string>(type: "text", nullable: true),
                    box_office = table.Column<string>(type: "text", nullable: true),
                    broadcast_range_end_year = table.Column<int>(type: "integer", nullable: true),
                    broadcast_range_raw = table.Column<string>(type: "text", nullable: true),
                    broadcast_range_start_year = table.Column<int>(type: "integer", nullable: true),
                    content_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    countries = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "ARRAY[]::text[]"),
                    director = table.Column<string>(type: "text", nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    genres = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "ARRAY[]::text[]"),
                    imdb_rating = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    imdb_votes = table.Column<long>(type: "bigint", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    media_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    metascore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    normalized_title = table.Column<string>(type: "text", nullable: false),
                    plot = table.Column<string>(type: "text", nullable: true),
                    poster_url = table.Column<string>(type: "text", nullable: true),
                    runtime = table.Column<string>(type: "text", nullable: true),
                    source_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    title = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_titles", x => x.id);
                    table.CheckConstraint("ck_titles_content_kind", "content_kind IS NULL OR content_kind IN ('standard', 'documentary', 'short')");
                    table.CheckConstraint("ck_titles_media_type", "media_type IN ('movie', 'series', 'documentary', 'short')");
                    table.CheckConstraint("ck_titles_provider_metadata_without_imdb_id", "imdb_id IS NULL OR (imdb_rating IS NULL AND imdb_votes IS NULL AND metascore IS NULL AND cardinality(genres) = 0 AND cardinality(countries) = 0 AND NULLIF(btrim(director), '') IS NULL AND NULLIF(btrim(plot), '') IS NULL AND NULLIF(btrim(poster_url), '') IS NULL AND NULLIF(btrim(runtime), '') IS NULL AND NULLIF(btrim(awards), '') IS NULL AND NULLIF(btrim(box_office), '') IS NULL)");
                    table.CheckConstraint("ck_titles_seen_range", "(first_seen_at IS NULL AND last_seen_at IS NULL) OR (first_seen_at IS NOT NULL AND last_seen_at IS NOT NULL AND first_seen_at <= last_seen_at)");
                    table.CheckConstraint("ck_titles_source_type", "source_type IS NULL OR source_type IN ('movie', 'series')");
                    table.ForeignKey(
                        name: "fk_titles_imdb_titles_imdb_id",
                        column: x => x.imdb_id,
                        principalTable: "imdb_titles",
                        principalColumn: "imdb_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "legacy_favorite_movies",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    title_id = table.Column<long>(type: "bigint", nullable: false),
                    added_from_catalog = table.Column<bool>(type: "boolean", nullable: false),
                    added_from_oscar = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    to_download = table.Column<bool>(type: "boolean", nullable: false),
                    to_watch = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_favorite_movies", x => new { x.user_id, x.title_id });
                    table.CheckConstraint("ck_favorite_movies_origin", "added_from_oscar OR added_from_catalog");
                    table.ForeignKey(
                        name: "fk_favorite_movies_titles_title_id",
                        column: x => x.title_id,
                        principalTable: "titles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_favorite_movies_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "occurrences",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    source_id = table.Column<long>(type: "bigint", nullable: false),
                    title_id = table.Column<long>(type: "bigint", nullable: false),
                    feed_entry_id = table.Column<string>(type: "text", nullable: true),
                    feed_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    quality = table.Column<string>(type: "text", nullable: true),
                    raw_title = table.Column<string>(type: "text", nullable: false),
                    rip_type = table.Column<string>(type: "text", nullable: true),
                    source_feed_name = table.Column<string>(type: "text", nullable: false),
                    source_item_key = table.Column<string>(type: "text", nullable: false),
                    source_published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    torrent_url = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occurrences", x => x.id);
                    table.UniqueConstraint("ak_occurrences_source_id_source_item_key", x => new { x.source_id, x.source_item_key });
                    table.CheckConstraint("ck_occurrences_seen_range", "first_seen_at <= last_seen_at");
                    table.ForeignKey(
                        name: "fk_occurrences_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_occurrences_titles_title_id",
                        column: x => x.title_id,
                        principalTable: "titles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "oscar_films",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    imdb_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    title_id = table.Column<long>(type: "bigint", nullable: false),
                    enrichment_attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    enrichment_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false, defaultValue: "pending"),
                    film_title = table.Column<string>(type: "text", nullable: false),
                    film_year = table.Column<int>(type: "integer", nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_enrichment_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_enrichment_error = table.Column<string>(type: "text", nullable: true),
                    next_enrichment_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    normalized_title = table.Column<string>(type: "text", nullable: false),
                    stable_key = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oscar_films", x => x.id);
                    table.CheckConstraint("ck_oscar_films_enrichment_attempt_count", "enrichment_attempt_count >= 0");
                    table.CheckConstraint("ck_oscar_films_enrichment_status", "enrichment_status IN ('pending', 'enriched', 'not_found', 'temporary_error')");
                    table.ForeignKey(
                        name: "fk_oscar_films_imdb_titles_imdb_id",
                        column: x => x.imdb_id,
                        principalTable: "imdb_titles",
                        principalColumn: "imdb_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_oscar_films_titles_title_id",
                        column: x => x.title_id,
                        principalTable: "titles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_oscar_nominations_oscar_film_id",
                table: "oscar_nominations",
                column: "oscar_film_id");

            migrationBuilder.CreateIndex(
                name: "ix_golden_globe_nominations_group_award",
                table: "golden_globe_nominations",
                columns: new[] { "group_id", "award_id" });

            migrationBuilder.CreateIndex(
                name: "IX_golden_globe_groups_imdb_id",
                table: "golden_globe_groups",
                column: "imdb_id");

            migrationBuilder.CreateIndex(
                name: "ux_golden_globe_groups_identity",
                table: "golden_globe_groups",
                columns: new[] { "title", "year", "nominee_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_legacy_favorite_movies_title_id",
                table: "legacy_favorite_movies",
                column: "title_id");

            migrationBuilder.CreateIndex(
                name: "ix_occurrences_title_id_last_seen_at",
                table: "occurrences",
                columns: new[] { "title_id", "last_seen_at" });

            migrationBuilder.CreateIndex(
                name: "ix_oscar_films_film_year",
                table: "oscar_films",
                column: "film_year");

            migrationBuilder.CreateIndex(
                name: "IX_oscar_films_imdb_id",
                table: "oscar_films",
                column: "imdb_id");

            migrationBuilder.CreateIndex(
                name: "IX_oscar_films_title_id",
                table: "oscar_films",
                column: "title_id");

            migrationBuilder.CreateIndex(
                name: "ux_oscar_films_stable_key",
                table: "oscar_films",
                column: "stable_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_titles_normalized_title_year_media_type",
                table: "titles",
                columns: new[] { "normalized_title", "year", "media_type" });

            migrationBuilder.CreateIndex(
                name: "ux_titles_id_imdb_id",
                table: "titles",
                columns: new[] { "id", "imdb_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_titles_imdb_id",
                table: "titles",
                column: "imdb_id",
                unique: true,
                filter: "imdb_id IS NOT NULL AND btrim(imdb_id) <> ''");

            migrationBuilder.AddForeignKey(
                name: "fk_golden_globe_nominations_groups_group_id",
                table: "golden_globe_nominations",
                column: "group_id",
                principalTable: "golden_globe_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_oscar_nominations_films_oscar_film_id",
                table: "oscar_nominations",
                column: "oscar_film_id",
                principalTable: "oscar_films",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
