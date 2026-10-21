using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeduplicateTorrentUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE torrent_releases
                SET torrent_url = 'https://rutracker.org/forum/viewtopic.php?t=' ||
                    ltrim(substring(lower(torrent_url) FROM '[?&]t=([0-9]+)'), '0')
                WHERE lower(torrent_url) ~ '^https?://(www[.])?rutracker[.](org|cc)/forum/viewtopic[.]php[?]'
                  AND ltrim(substring(lower(torrent_url) FROM '[?&]t=([0-9]+)'), '0') <> '';

                DO $$
                DECLARE duplicate_release_count bigint;
                BEGIN
                    SELECT COUNT(*) INTO duplicate_release_count
                    FROM (
                        SELECT ROW_NUMBER() OVER (
                            PARTITION BY torrent_url
                            ORDER BY last_seen_at DESC, id DESC) AS duplicate_rank
                        FROM torrent_releases
                    ) AS ranked_releases
                    WHERE duplicate_rank > 1;

                    RAISE NOTICE 'Deduplicating torrent URLs will remove % older torrent release rows',
                        duplicate_release_count;
                END $$;

                WITH ranked_releases AS (
                    SELECT source_id, source_item_key, id,
                        ROW_NUMBER() OVER (
                            PARTITION BY torrent_url
                            ORDER BY last_seen_at DESC, id DESC) AS duplicate_rank
                    FROM torrent_releases
                )
                DELETE FROM rss_item_states AS item_state
                USING ranked_releases
                WHERE ranked_releases.duplicate_rank > 1
                  AND item_state.source_id = ranked_releases.source_id
                  AND item_state.source_item_key = ranked_releases.source_item_key;

                WITH ranked_releases AS (
                    SELECT id,
                        ROW_NUMBER() OVER (
                            PARTITION BY torrent_url
                            ORDER BY last_seen_at DESC, id DESC) AS duplicate_rank
                    FROM torrent_releases
                )
                DELETE FROM torrent_releases AS release
                USING ranked_releases
                WHERE release.id = ranked_releases.id
                  AND ranked_releases.duplicate_rank > 1;

                DO $$
                DECLARE orphaned_movie_count bigint;
                BEGIN
                    SELECT COUNT(*) INTO orphaned_movie_count
                    FROM torrent_movies AS movie
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM torrent_releases AS release
                        WHERE release.torrent_movie_id = movie.id);

                    RAISE NOTICE 'Cleanup will remove % torrent catalog rows with no remaining releases',
                        orphaned_movie_count;
                END $$;

                DELETE FROM torrent_movies AS movie
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM torrent_releases AS release
                    WHERE release.torrent_movie_id = movie.id);
                """);

            migrationBuilder.DropUniqueConstraint(
                name: "ak_torrent_releases_source_id_source_item_key",
                table: "torrent_releases");

            migrationBuilder.CreateIndex(
                name: "ux_torrent_releases_source_id_source_item_key",
                table: "torrent_releases",
                columns: new[] { "source_id", "source_item_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_torrent_releases_torrent_url",
                table: "torrent_releases",
                column: "torrent_url",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_torrent_releases_source_id_source_item_key",
                table: "torrent_releases");

            migrationBuilder.DropIndex(
                name: "ux_torrent_releases_torrent_url",
                table: "torrent_releases");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_torrent_releases_source_id_source_item_key",
                table: "torrent_releases",
                columns: new[] { "source_id", "source_item_key" });
        }
    }
}
