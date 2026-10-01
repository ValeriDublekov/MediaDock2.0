using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssociateParseLogsWithScanRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "scan_run_id",
                table: "parse_logs",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_parse_logs_scan_run_id",
                table: "parse_logs",
                column: "scan_run_id");

            migrationBuilder.AddForeignKey(
                name: "fk_parse_logs_scan_runs_scan_run_id",
                table: "parse_logs",
                column: "scan_run_id",
                principalTable: "scan_runs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_parse_logs_scan_runs_scan_run_id",
                table: "parse_logs");

            migrationBuilder.DropIndex(
                name: "IX_parse_logs_scan_run_id",
                table: "parse_logs");

            migrationBuilder.DropColumn(
                name: "scan_run_id",
                table: "parse_logs");
        }
    }
}
