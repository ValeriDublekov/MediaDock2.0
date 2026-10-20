using MediaDock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MediaDockDbContext))]
[Migration("20261020140000_EnforceScanRunNonnegativeCounters")]
public partial class EnforceScanRunNonnegativeCounters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddCheckConstraint(
            name: "ck_scan_runs_nonnegative_counters",
            table: "scan_runs",
            sql: "feeds_processed >= 0 AND entries_seen >= 0 AND known_entries_skipped >= 0 AND titles_created >= 0 AND occurrences_created >= 0 AND cache_hits >= 0 AND omdb_requests >= 0 AND ignored_entries >= 0 AND error_count >= 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_scan_runs_nonnegative_counters",
            table: "scan_runs");
    }
}