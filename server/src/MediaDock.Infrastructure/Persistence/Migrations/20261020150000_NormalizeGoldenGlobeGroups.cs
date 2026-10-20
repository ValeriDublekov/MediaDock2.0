using MediaDock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MediaDockDbContext))]
[Migration("20261020150000_NormalizeGoldenGlobeGroups")]
public partial class NormalizeGoldenGlobeGroups : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM golden_globe_nominations
                    GROUP BY title, year, nominee_type
                    HAVING COUNT(DISTINCT ROW(
                        imdb_id,
                        is_imdb_id_manual,
                        imdb_id_version,
                        enrichment_status,
                        enrichment_attempt_count,
                        last_enrichment_attempt_at,
                        next_enrichment_attempt_at,
                        last_enrichment_error
                    )) > 1
                ) THEN
                    RAISE EXCEPTION 'Cannot normalize Golden Globes groups: conflicting IMDb links or enrichment states exist for one or more (title, year, nominee_type) groups. Reconcile the nominations and retry.';
                END IF;
            END $$;
            """);

        migrationBuilder.CreateTable(
            name: "golden_globe_groups",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                year = table.Column<int>(type: "integer", nullable: false),
                nominee_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                imdb_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                is_imdb_id_manual = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                imdb_id_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                enrichment_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                enrichment_attempt_count = table.Column<int>(type: "integer", nullable: false),
                last_enrichment_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                next_enrichment_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_enrichment_error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_golden_globe_groups", x => x.id);
                table.ForeignKey(
                    name: "fk_golden_globe_groups_imdb_titles_imdb_id",
                    column: x => x.imdb_id,
                    principalTable: "imdb_titles",
                    principalColumn: "imdb_id",
                    onDelete: ReferentialAction.Restrict);
                table.CheckConstraint(
                    "ck_golden_globe_groups_enrichment_status",
                    "enrichment_status IN ('pending', 'enriched', 'problem', 'not_found', 'temporary_error')");
                table.CheckConstraint("ck_golden_globe_groups_enrichment_attempt_count", "enrichment_attempt_count >= 0");
                table.CheckConstraint("ck_golden_globe_groups_nominee_type", "nominee_type IN ('movie', 'series')");
            });

        migrationBuilder.AddColumn<long>(
            name: "group_id",
            table: "golden_globe_nominations",
            type: "bigint",
            nullable: true);

        migrationBuilder.Sql(
            """
            INSERT INTO golden_globe_groups (
                title, year, nominee_type, imdb_id, is_imdb_id_manual, imdb_id_version,
                enrichment_status, enrichment_attempt_count, last_enrichment_attempt_at,
                next_enrichment_attempt_at, last_enrichment_error)
            SELECT title, year, nominee_type, imdb_id, is_imdb_id_manual, imdb_id_version,
                   enrichment_status, enrichment_attempt_count, last_enrichment_attempt_at,
                   next_enrichment_attempt_at, last_enrichment_error
            FROM golden_globe_nominations
            GROUP BY title, year, nominee_type, imdb_id, is_imdb_id_manual, imdb_id_version,
                     enrichment_status, enrichment_attempt_count, last_enrichment_attempt_at,
                     next_enrichment_attempt_at, last_enrichment_error;

            UPDATE golden_globe_nominations AS nomination
            SET group_id = group_row.id
            FROM golden_globe_groups AS group_row
            WHERE group_row.title = nomination.title
              AND group_row.year = nomination.year
              AND group_row.nominee_type = nomination.nominee_type;
            """);

        migrationBuilder.AlterColumn<long>(
            name: "group_id",
            table: "golden_globe_nominations",
            type: "bigint",
            nullable: false,
            oldClrType: typeof(long),
            oldType: "bigint",
            oldNullable: true);

        migrationBuilder.AddForeignKey(
            name: "fk_golden_globe_nominations_groups_group_id",
            table: "golden_globe_nominations",
            column: "group_id",
            principalTable: "golden_globe_groups",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.DropForeignKey(
            name: "fk_golden_globe_nominations_imdb_titles_imdb_id",
            table: "golden_globe_nominations");
        migrationBuilder.DropIndex(
            name: "IX_golden_globe_nominations_imdb_id",
            table: "golden_globe_nominations");
        migrationBuilder.DropIndex(
            name: "ix_golden_globe_nominations_year_award",
            table: "golden_globe_nominations");

        foreach (var column in new[]
        {
            "year", "title", "nominee_type", "imdb_id", "is_imdb_id_manual", "imdb_id_version",
            "enrichment_status", "enrichment_attempt_count", "last_enrichment_attempt_at",
            "next_enrichment_attempt_at", "last_enrichment_error"
        })
        {
            migrationBuilder.DropColumn(name: column, table: "golden_globe_nominations");
        }

        migrationBuilder.CreateIndex(
            name: "ux_golden_globe_groups_identity",
            table: "golden_globe_groups",
            columns: new[] { "title", "year", "nominee_type" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_golden_globe_groups_imdb_id",
            table: "golden_globe_groups",
            column: "imdb_id");
        migrationBuilder.CreateIndex(
            name: "ix_golden_globe_nominations_group_award",
            table: "golden_globe_nominations",
            columns: new[] { "group_id", "award_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("year", "golden_globe_nominations", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>("title", "golden_globe_nominations", type: "character varying(1000)", maxLength: 1000, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("nominee_type", "golden_globe_nominations", type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "movie");
        migrationBuilder.AddColumn<string>("imdb_id", "golden_globe_nominations", type: "character varying(32)", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<bool>("is_imdb_id_manual", "golden_globe_nominations", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<int>("imdb_id_version", "golden_globe_nominations", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>("enrichment_status", "golden_globe_nominations", type: "character varying(24)", maxLength: 24, nullable: false, defaultValue: "pending");
        migrationBuilder.AddColumn<int>("enrichment_attempt_count", "golden_globe_nominations", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<DateTimeOffset>("last_enrichment_attempt_at", "golden_globe_nominations", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("next_enrichment_attempt_at", "golden_globe_nominations", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>("last_enrichment_error", "golden_globe_nominations", type: "text", nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE golden_globe_nominations AS nomination
            SET title = group_row.title,
                year = group_row.year,
                nominee_type = group_row.nominee_type,
                imdb_id = group_row.imdb_id,
                is_imdb_id_manual = group_row.is_imdb_id_manual,
                imdb_id_version = group_row.imdb_id_version,
                enrichment_status = group_row.enrichment_status,
                enrichment_attempt_count = group_row.enrichment_attempt_count,
                last_enrichment_attempt_at = group_row.last_enrichment_attempt_at,
                next_enrichment_attempt_at = group_row.next_enrichment_attempt_at,
                last_enrichment_error = group_row.last_enrichment_error
            FROM golden_globe_groups AS group_row
            WHERE group_row.id = nomination.group_id;
            """);

        migrationBuilder.DropForeignKey(
            name: "fk_golden_globe_nominations_groups_group_id",
            table: "golden_globe_nominations");
        migrationBuilder.DropIndex(
            name: "ix_golden_globe_nominations_group_award",
            table: "golden_globe_nominations");
        migrationBuilder.DropColumn(name: "group_id", table: "golden_globe_nominations");
        migrationBuilder.DropTable(name: "golden_globe_groups");

        migrationBuilder.AddCheckConstraint(
            name: "ck_golden_globe_nominations_enrichment_status",
            table: "golden_globe_nominations",
            sql: "enrichment_status IN ('pending', 'enriched', 'problem', 'not_found', 'temporary_error')");
        migrationBuilder.AddCheckConstraint(
            name: "ck_golden_globe_nominations_enrichment_attempt_count",
            table: "golden_globe_nominations",
            sql: "enrichment_attempt_count >= 0");
        migrationBuilder.AddCheckConstraint(
            name: "ck_golden_globe_nominations_nominee_type",
            table: "golden_globe_nominations",
            sql: "nominee_type IN ('movie', 'series')");
        migrationBuilder.CreateIndex(
            name: "IX_golden_globe_nominations_imdb_id",
            table: "golden_globe_nominations",
            column: "imdb_id");
        migrationBuilder.CreateIndex(
            name: "ix_golden_globe_nominations_year_award",
            table: "golden_globe_nominations",
            columns: new[] { "year", "award_id" });
        migrationBuilder.AddForeignKey(
            name: "fk_golden_globe_nominations_imdb_titles_imdb_id",
            table: "golden_globe_nominations",
            column: "imdb_id",
            principalTable: "imdb_titles",
            principalColumn: "imdb_id",
            onDelete: ReferentialAction.Restrict);
    }
}