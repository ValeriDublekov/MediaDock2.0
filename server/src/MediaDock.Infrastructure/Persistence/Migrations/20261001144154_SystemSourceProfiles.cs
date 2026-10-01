using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SystemSourceProfiles : Migration
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
            migrationBuilder.Sql("""
                UPDATE sources
                SET name = CASE feed_type
                    WHEN 'movie' THEN 'Movies'
                    WHEN 'series_complete' THEN 'Complete seasons'
                    WHEN 'series_ongoing' THEN 'Ongoing episodes'
                END,
                is_enabled = TRUE;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_sources_feed_type",
                table: "sources",
                sql: "feed_type IN ('movie', 'series_complete', 'series_ongoing')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sources_feed_type",
                table: "sources");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sources_feed_type",
                table: "sources",
                sql: "feed_type IN ('movie', 'series_complete', 'series_ongoing')");
        }
    }
}
