using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SourceOwnedMovieIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    discard_count bigint;
                BEGIN
                    SELECT COUNT(*) INTO discard_count
                    FROM favorite_movies AS favorite
                    JOIN titles AS title ON title.id = favorite.title_id
                    LEFT JOIN imdb_titles AS imdb ON imdb.imdb_id = title.imdb_id
                    WHERE imdb.metadata_status IS DISTINCT FROM 'enriched';

                    RAISE NOTICE 'Source-owned movie migration will delete % non-transferable favorite rows', discard_count;
                END $$;
                """);

            migrationBuilder.RenameTable(name: "favorite_movies", newName: "legacy_favorite_movies");
            migrationBuilder.RenameIndex(
                name: "IX_favorite_movies_title_id",
                table: "legacy_favorite_movies",
                newName: "IX_legacy_favorite_movies_title_id");

            migrationBuilder.AddColumn<long>(
                name: "oscar_movie_id",
                table: "oscar_nominations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "golden_globe_title_id",
                table: "golden_globe_nominations",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "golden_globe_titles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    nominee_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    imdb_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    is_imdb_id_manual = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    imdb_id_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    enrichment_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    enrichment_attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_enrichment_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_enrichment_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_enrichment_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_golden_globe_titles", x => x.id);
                    table.CheckConstraint("ck_golden_globe_titles_enrichment_attempt_count", "enrichment_attempt_count >= 0");
                    table.CheckConstraint("ck_golden_globe_titles_enrichment_status", "enrichment_status IN ('pending', 'enriched', 'problem', 'not_found', 'temporary_error')");
                    table.CheckConstraint("ck_golden_globe_titles_nominee_type", "nominee_type IN ('movie', 'series')");
                    table.ForeignKey(
                        name: "fk_golden_globe_titles_imdb_titles_imdb_id",
                        column: x => x.imdb_id,
                        principalTable: "imdb_titles",
                        principalColumn: "imdb_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "favorite_movies",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    imdb_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    to_watch = table.Column<bool>(type: "boolean", nullable: false),
                    to_download = table.Column<bool>(type: "boolean", nullable: false),
                    added_from_oscar = table.Column<bool>(type: "boolean", nullable: false),
                    added_from_catalog = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_favorite_movies_imdb", x => new { x.user_id, x.imdb_id });
                    table.CheckConstraint("ck_favorite_movies_origin", "added_from_oscar OR added_from_catalog");
                    table.ForeignKey(
                        name: "fk_favorite_movies_imdb_titles_imdb_id",
                        column: x => x.imdb_id,
                        principalTable: "imdb_titles",
                        principalColumn: "imdb_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_favorite_movies_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "oscar_movies",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    stable_key = table.Column<string>(type: "text", nullable: false),
                    film_title = table.Column<string>(type: "text", nullable: false),
                    normalized_title = table.Column<string>(type: "text", nullable: false),
                    film_year = table.Column<int>(type: "integer", nullable: false),
                    imdb_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    enrichment_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false, defaultValue: "pending"),
                    enrichment_attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_enrichment_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_enrichment_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_enrichment_error = table.Column<string>(type: "text", nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oscar_movies", x => x.id);
                    table.CheckConstraint("ck_oscar_movies_enrichment_attempt_count", "enrichment_attempt_count >= 0");
                    table.CheckConstraint("ck_oscar_movies_enrichment_status", "enrichment_status IN ('pending', 'enriched', 'not_found', 'temporary_error')");
                    table.ForeignKey(
                        name: "fk_oscar_movies_imdb_titles_imdb_id",
                        column: x => x.imdb_id,
                        principalTable: "imdb_titles",
                        principalColumn: "imdb_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "torrent_movies",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    title = table.Column<string>(type: "text", nullable: false),
                    normalized_title = table.Column<string>(type: "text", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: true),
                    media_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    content_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    broadcast_range_start_year = table.Column<int>(type: "integer", nullable: true),
                    broadcast_range_end_year = table.Column<int>(type: "integer", nullable: true),
                    broadcast_range_raw = table.Column<string>(type: "text", nullable: true),
                    imdb_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_torrent_movies", x => x.id);
                    table.CheckConstraint("ck_torrent_movies_content_kind", "content_kind IS NULL OR content_kind IN ('standard', 'documentary', 'short')");
                    table.CheckConstraint("ck_torrent_movies_media_type", "media_type IN ('movie', 'series', 'documentary', 'short')");
                    table.CheckConstraint("ck_torrent_movies_seen_range", "(first_seen_at IS NULL AND last_seen_at IS NULL) OR (first_seen_at IS NOT NULL AND last_seen_at IS NOT NULL AND first_seen_at <= last_seen_at)");
                    table.CheckConstraint("ck_torrent_movies_source_type", "source_type IS NULL OR source_type IN ('movie', 'series')");
                    table.ForeignKey(
                        name: "fk_torrent_movies_imdb_titles_imdb_id",
                        column: x => x.imdb_id,
                        principalTable: "imdb_titles",
                        principalColumn: "imdb_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "torrent_releases",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    torrent_movie_id = table.Column<long>(type: "bigint", nullable: false),
                    source_id = table.Column<long>(type: "bigint", nullable: false),
                    source_item_key = table.Column<string>(type: "text", nullable: false),
                    feed_entry_id = table.Column<string>(type: "text", nullable: true),
                    torrent_url = table.Column<string>(type: "text", nullable: false),
                    raw_title = table.Column<string>(type: "text", nullable: false),
                    source_feed_name = table.Column<string>(type: "text", nullable: false),
                    feed_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    source_published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    quality = table.Column<string>(type: "text", nullable: true),
                    rip_type = table.Column<string>(type: "text", nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_torrent_releases", x => x.id);
                    table.UniqueConstraint("ak_torrent_releases_source_id_source_item_key", x => new { x.source_id, x.source_item_key });
                    table.CheckConstraint("ck_torrent_releases_seen_range", "first_seen_at <= last_seen_at");
                    table.ForeignKey(
                        name: "fk_torrent_releases_movies_torrent_movie_id",
                        column: x => x.torrent_movie_id,
                        principalTable: "torrent_movies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_torrent_releases_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_oscar_nominations_oscar_movie_id",
                table: "oscar_nominations",
                column: "oscar_movie_id");

            migrationBuilder.CreateIndex(
                name: "IX_golden_globe_nominations_golden_globe_title_id",
                table: "golden_globe_nominations",
                column: "golden_globe_title_id");

            migrationBuilder.CreateIndex(
                name: "IX_favorite_movies_imdb_id",
                table: "favorite_movies",
                column: "imdb_id");

            migrationBuilder.CreateIndex(
                name: "ix_golden_globe_titles_imdb_id",
                table: "golden_globe_titles",
                column: "imdb_id");

            migrationBuilder.CreateIndex(
                name: "ux_golden_globe_titles_identity",
                table: "golden_globe_titles",
                columns: new[] { "title", "year", "nominee_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_oscar_movies_film_year",
                table: "oscar_movies",
                column: "film_year");

            migrationBuilder.CreateIndex(
                name: "IX_oscar_movies_imdb_id",
                table: "oscar_movies",
                column: "imdb_id");

            migrationBuilder.CreateIndex(
                name: "ux_oscar_movies_stable_key",
                table: "oscar_movies",
                column: "stable_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_torrent_movies_normalized_title_year_media_type",
                table: "torrent_movies",
                columns: new[] { "normalized_title", "year", "media_type" });

            migrationBuilder.CreateIndex(
                name: "ux_torrent_movies_imdb_id",
                table: "torrent_movies",
                column: "imdb_id",
                unique: true,
                filter: "imdb_id IS NOT NULL AND btrim(imdb_id) <> ''");

            migrationBuilder.CreateIndex(
                name: "ix_torrent_releases_torrent_movie_id_last_seen_at",
                table: "torrent_releases",
                columns: new[] { "torrent_movie_id", "last_seen_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_golden_globe_nominations_titles_title_id",
                table: "golden_globe_nominations",
                column: "golden_globe_title_id",
                principalTable: "golden_globe_titles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_oscar_nominations_movies_oscar_movie_id",
                table: "oscar_nominations",
                column: "oscar_movie_id",
                principalTable: "oscar_movies",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                INSERT INTO oscar_movies (
                    id, stable_key, film_title, normalized_title, film_year, imdb_id, enrichment_status,
                    enrichment_attempt_count, last_enrichment_attempt_at, next_enrichment_attempt_at,
                    last_enrichment_error, imported_at, updated_at)
                SELECT id, stable_key, film_title, normalized_title, film_year, imdb_id, enrichment_status,
                       enrichment_attempt_count, last_enrichment_attempt_at, next_enrichment_attempt_at,
                       last_enrichment_error, imported_at, updated_at
                FROM oscar_films;

                UPDATE oscar_nominations
                SET oscar_movie_id = oscar_film_id;

                INSERT INTO golden_globe_titles (
                    id, title, year, nominee_type, imdb_id, is_imdb_id_manual, imdb_id_version,
                    enrichment_status, enrichment_attempt_count, last_enrichment_attempt_at,
                    next_enrichment_attempt_at, last_enrichment_error)
                SELECT id, title, year, nominee_type, imdb_id, is_imdb_id_manual, imdb_id_version,
                       enrichment_status, enrichment_attempt_count, last_enrichment_attempt_at,
                       next_enrichment_attempt_at, last_enrichment_error
                FROM golden_globe_groups;

                UPDATE golden_globe_nominations
                SET golden_globe_title_id = group_id;

                INSERT INTO torrent_movies (
                    id, title, normalized_title, year, media_type, source_type, content_kind,
                    broadcast_range_start_year, broadcast_range_end_year, broadcast_range_raw,
                    imdb_id, first_seen_at, last_seen_at, updated_at)
                SELECT title.id, title.title, title.normalized_title, title.year, title.media_type,
                       title.source_type, title.content_kind, title.broadcast_range_start_year,
                       title.broadcast_range_end_year, title.broadcast_range_raw, title.imdb_id,
                       title.first_seen_at, title.last_seen_at, title.updated_at
                FROM titles AS title
                WHERE EXISTS (SELECT 1 FROM occurrences AS occurrence WHERE occurrence.title_id = title.id);

                INSERT INTO torrent_releases (
                    id, torrent_movie_id, source_id, source_item_key, feed_entry_id, torrent_url,
                    raw_title, source_feed_name, feed_type, source_published_at, observed_at, quality,
                    rip_type, first_seen_at, last_seen_at)
                SELECT occurrence.id, occurrence.title_id, occurrence.source_id, occurrence.source_item_key,
                       occurrence.feed_entry_id, occurrence.torrent_url, occurrence.raw_title,
                       occurrence.source_feed_name, occurrence.feed_type, occurrence.source_published_at,
                       occurrence.observed_at, occurrence.quality, occurrence.rip_type,
                       occurrence.first_seen_at, occurrence.last_seen_at
                FROM occurrences AS occurrence;

                INSERT INTO favorite_movies (
                    user_id, imdb_id, to_watch, to_download, added_from_oscar, added_from_catalog,
                    created_at, updated_at)
                SELECT favorite.user_id, title.imdb_id, bool_or(favorite.to_watch),
                       bool_or(favorite.to_download), bool_or(favorite.added_from_oscar),
                       bool_or(favorite.added_from_catalog), min(favorite.created_at), max(favorite.updated_at)
                FROM legacy_favorite_movies AS favorite
                JOIN titles AS title ON title.id = favorite.title_id
                JOIN imdb_titles AS imdb
                  ON imdb.imdb_id = title.imdb_id AND imdb.metadata_status = 'enriched'
                GROUP BY favorite.user_id, title.imdb_id;

                DELETE FROM legacy_favorite_movies AS favorite
                USING titles AS title
                WHERE title.id = favorite.title_id
                  AND NOT EXISTS (
                      SELECT 1 FROM imdb_titles AS imdb
                      WHERE imdb.imdb_id = title.imdb_id AND imdb.metadata_status = 'enriched');

                SELECT setval(pg_get_serial_sequence('oscar_movies', 'id'), COALESCE(MAX(id), 1), MAX(id) IS NOT NULL)
                FROM oscar_movies;
                SELECT setval(pg_get_serial_sequence('golden_globe_titles', 'id'), COALESCE(MAX(id), 1), MAX(id) IS NOT NULL)
                FROM golden_globe_titles;
                SELECT setval(pg_get_serial_sequence('torrent_movies', 'id'), COALESCE(MAX(id), 1), MAX(id) IS NOT NULL)
                FROM torrent_movies;
                SELECT setval(pg_get_serial_sequence('torrent_releases', 'id'), COALESCE(MAX(id), 1), MAX(id) IS NOT NULL)
                FROM torrent_releases;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_golden_globe_nominations_titles_title_id",
                table: "golden_globe_nominations");

            migrationBuilder.DropForeignKey(
                name: "fk_oscar_nominations_movies_oscar_movie_id",
                table: "oscar_nominations");

            migrationBuilder.DropTable(
                name: "golden_globe_titles");

            migrationBuilder.DropTable(
                name: "favorite_movies");

            migrationBuilder.RenameTable(name: "legacy_favorite_movies", newName: "favorite_movies");
            migrationBuilder.RenameIndex(
                name: "IX_legacy_favorite_movies_title_id",
                table: "favorite_movies",
                newName: "IX_favorite_movies_title_id");

            migrationBuilder.DropTable(
                name: "oscar_movies");

            migrationBuilder.DropTable(
                name: "torrent_releases");

            migrationBuilder.DropTable(
                name: "torrent_movies");

            migrationBuilder.DropIndex(
                name: "IX_oscar_nominations_oscar_movie_id",
                table: "oscar_nominations");

            migrationBuilder.DropIndex(
                name: "IX_golden_globe_nominations_golden_globe_title_id",
                table: "golden_globe_nominations");

            migrationBuilder.DropColumn(
                name: "oscar_movie_id",
                table: "oscar_nominations");

            migrationBuilder.DropColumn(
                name: "golden_globe_title_id",
                table: "golden_globe_nominations");

        }
    }
}
