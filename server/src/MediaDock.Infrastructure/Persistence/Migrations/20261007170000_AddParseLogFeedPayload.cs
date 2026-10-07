using MediaDock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MediaDockDbContext))]
[Migration("20261007170000_AddParseLogFeedPayload")]
public partial class AddParseLogFeedPayload : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "feed_entry_id",
            table: "parse_logs",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "torrent_url",
            table: "parse_logs",
            type: "text",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "feed_entry_id", table: "parse_logs");
        migrationBuilder.DropColumn(name: "torrent_url", table: "parse_logs");
    }
}