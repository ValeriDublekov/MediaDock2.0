using MediaDock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediaDock.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MediaDockDbContext))]
[Migration("20261001160000_AddOmdbDailyErrorCode")]
public sealed class AddOmdbDailyErrorCode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "last_error_code",
            table: "omdb_daily_usage",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "daily_request_limit_reached",
            table: "omdb_daily_usage",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "last_error_code",
            table: "omdb_daily_usage");

        migrationBuilder.DropColumn(
            name: "daily_request_limit_reached",
            table: "omdb_daily_usage");
    }
}