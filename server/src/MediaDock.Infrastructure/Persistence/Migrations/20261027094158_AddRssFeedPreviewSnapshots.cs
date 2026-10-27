using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRssFeedPreviewSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rss_feed_preview_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    profile_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    cached_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    feeds_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rss_feed_preview_snapshots", x => x.id);
                    table.CheckConstraint("ck_rss_feed_preview_snapshots_profile_id", "profile_id IN ('movie', 'series_complete', 'series_ongoing')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_rss_feed_preview_snapshots_profile_cached_at_id",
                table: "rss_feed_preview_snapshots",
                columns: new[] { "profile_id", "cached_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rss_feed_preview_snapshots");
        }
    }
}
