using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParseLogLookupTitles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "lookup_titles",
                table: "parse_logs",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::text[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lookup_titles",
                table: "parse_logs");
        }
    }
}
