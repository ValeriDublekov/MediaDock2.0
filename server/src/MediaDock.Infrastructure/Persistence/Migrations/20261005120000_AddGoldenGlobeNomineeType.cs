using MediaDock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace MediaDock.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MediaDockDbContext))]
[Migration("20261005120000_AddGoldenGlobeNomineeType")]
public partial class AddGoldenGlobeNomineeType : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "nominee_type",
            table: "golden_globe_nominations",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "movie");

        migrationBuilder.AddCheckConstraint(
            name: "ck_golden_globe_nominations_nominee_type",
            table: "golden_globe_nominations",
            sql: "nominee_type IN ('movie', 'series')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_golden_globe_nominations_nominee_type",
            table: "golden_globe_nominations");

        migrationBuilder.DropColumn(
            name: "nominee_type",
            table: "golden_globe_nominations");
    }
}