using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRssItemProcessingStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sources_feed_type",
                table: "sources");

            migrationBuilder.Sql("UPDATE sources SET feed_type = 'series_ongoing' WHERE feed_type = 'series';");
            migrationBuilder.Sql("UPDATE occurrences SET feed_type = 'series_ongoing' WHERE feed_type = 'series';");
            migrationBuilder.Sql("UPDATE parse_logs SET feed_type = 'series_ongoing' WHERE feed_type = 'series';");

            migrationBuilder.AddColumn<int>(
                name: "known_entries_skipped",
                table: "scan_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "rss_item_states",
                columns: table => new
                {
                    source_id = table.Column<long>(type: "bigint", nullable: false),
                    source_item_key = table.Column<string>(type: "text", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    disposition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rss_item_states", x => new { x.source_id, x.source_item_key });
                    table.CheckConstraint("ck_rss_item_states_disposition", "disposition IN ('resolved', 'terminal')");
                    table.CheckConstraint("ck_rss_item_states_expiry", "disposition = 'resolved' OR (disposition = 'terminal' AND expires_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_rss_item_states_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_sources_feed_type",
                table: "sources",
                sql: "feed_type IN ('movie', 'series_complete', 'series_ongoing')");

            migrationBuilder.CreateIndex(
                name: "ix_rss_item_states_expires_at",
                table: "rss_item_states",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rss_item_states");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sources_feed_type",
                table: "sources");

            migrationBuilder.Sql("UPDATE sources SET feed_type = 'series' WHERE feed_type IN ('series_complete', 'series_ongoing');");
            migrationBuilder.Sql("UPDATE occurrences SET feed_type = 'series' WHERE feed_type IN ('series_complete', 'series_ongoing');");
            migrationBuilder.Sql("UPDATE parse_logs SET feed_type = 'series' WHERE feed_type IN ('series_complete', 'series_ongoing');");

            migrationBuilder.DropColumn(
                name: "known_entries_skipped",
                table: "scan_runs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sources_feed_type",
                table: "sources",
                sql: "feed_type IN ('movie', 'series')");
        }
    }
}
