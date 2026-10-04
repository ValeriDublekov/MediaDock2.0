using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations;

public partial class AddGoldenGlobes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("ck_background_jobs_input_type", "background_jobs");
        migrationBuilder.DropCheckConstraint("ck_background_jobs_type", "background_jobs");
        migrationBuilder.DropCheckConstraint("ck_background_jobs_type_trigger", "background_jobs");
        migrationBuilder.AddCheckConstraint("ck_background_jobs_input_type", "background_jobs", "(job_type IN ('oscar_import', 'golden_globe_import') AND (input_bytes IS NOT NULL OR status IN ('succeeded', 'partial', 'failed'))) OR (job_type IN ('rss_scan', 'oscar_enrichment') AND input_bytes IS NULL)");
        migrationBuilder.AddCheckConstraint("ck_background_jobs_type", "background_jobs", "job_type IN ('rss_scan', 'oscar_import', 'golden_globe_import', 'oscar_enrichment')");
        migrationBuilder.AddCheckConstraint("ck_background_jobs_type_trigger", "background_jobs", "(job_type = 'rss_scan' AND trigger IN ('manual', 'schedule')) OR (job_type IN ('oscar_import', 'golden_globe_import', 'oscar_enrichment') AND trigger = 'manual')");

        migrationBuilder.CreateTable("golden_globe_awards", table => new
        {
            id = table.Column<long>(type: "bigint", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
        }, constraints: table => table.PrimaryKey("pk_golden_globe_awards", x => x.id));
        migrationBuilder.CreateTable("golden_globe_nominations", table => new
        {
            id = table.Column<long>(type: "bigint", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            import_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            year = table.Column<int>(type: "integer", nullable: false),
            winner = table.Column<bool>(type: "boolean", nullable: false),
            award_id = table.Column<long>(type: "bigint", nullable: false),
            title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
            imdb_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
        }, constraints: table => { table.PrimaryKey("pk_golden_globe_nominations", x => x.id); table.ForeignKey("fk_golden_globe_nominations_awards_award_id", x => x.award_id, "golden_globe_awards", "id", onDelete: ReferentialAction.Restrict); });
        migrationBuilder.CreateIndex("ux_golden_globe_awards_name", "golden_globe_awards", "name", unique: true);
        migrationBuilder.CreateIndex("ux_golden_globe_nominations_import_key", "golden_globe_nominations", "import_key", unique: true);
        migrationBuilder.CreateIndex("ix_golden_globe_nominations_year_award", "golden_globe_nominations", new[] { "year", "award_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("golden_globe_nominations");
        migrationBuilder.DropTable("golden_globe_awards");
        migrationBuilder.DropCheckConstraint("ck_background_jobs_input_type", "background_jobs");
        migrationBuilder.DropCheckConstraint("ck_background_jobs_type", "background_jobs");
        migrationBuilder.DropCheckConstraint("ck_background_jobs_type_trigger", "background_jobs");
        migrationBuilder.AddCheckConstraint("ck_background_jobs_input_type", "background_jobs", "(job_type = 'oscar_import' AND (input_bytes IS NOT NULL OR status IN ('succeeded', 'partial', 'failed'))) OR (job_type IN ('rss_scan', 'oscar_enrichment') AND input_bytes IS NULL)");
        migrationBuilder.AddCheckConstraint("ck_background_jobs_type", "background_jobs", "job_type IN ('rss_scan', 'oscar_import', 'oscar_enrichment')");
        migrationBuilder.AddCheckConstraint("ck_background_jobs_type_trigger", "background_jobs", "(job_type = 'rss_scan' AND trigger IN ('manual', 'schedule')) OR (job_type IN ('oscar_import', 'oscar_enrichment') AND trigger = 'manual')");
    }
}
