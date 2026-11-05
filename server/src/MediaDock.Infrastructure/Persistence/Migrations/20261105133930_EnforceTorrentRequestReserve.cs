using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceTorrentRequestReserve : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_omdb_daily_usage_counts",
                table: "omdb_daily_usage");

            migrationBuilder.AddColumn<int>(
                name: "torrent_requests",
                table: "omdb_daily_usage",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "ck_omdb_daily_usage_counts",
                table: "omdb_daily_usage",
                sql: "total_requests >= 0 AND torrent_requests >= 0 AND oscar_requests >= 0 AND torrent_requests + oscar_requests <= total_requests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_omdb_daily_usage_counts",
                table: "omdb_daily_usage");

            migrationBuilder.DropColumn(
                name: "torrent_requests",
                table: "omdb_daily_usage");

            migrationBuilder.AddCheckConstraint(
                name: "ck_omdb_daily_usage_counts",
                table: "omdb_daily_usage",
                sql: "total_requests >= 0 AND oscar_requests >= 0 AND oscar_requests <= total_requests");
        }
    }
}
