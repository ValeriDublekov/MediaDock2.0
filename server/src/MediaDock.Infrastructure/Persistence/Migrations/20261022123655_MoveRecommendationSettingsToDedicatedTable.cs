using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveRecommendationSettingsToDedicatedTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "general_recommendation_settings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    median_period_years = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    requires_vote_recalculation = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    last_vote_recalculation_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_vote_recalculation_period_years = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_general_recommendation_settings", x => x.id);
                    table.CheckConstraint("ck_general_recommendation_settings_last_vote_recalculation_per~", "last_vote_recalculation_period_years IS NULL OR last_vote_recalculation_period_years > 0");
                    table.CheckConstraint("ck_general_recommendation_settings_median_period_years", "median_period_years > 0");
                    table.CheckConstraint("ck_general_recommendation_settings_requires_vote_recalculation", "requires_vote_recalculation IN (TRUE, FALSE)");
                    table.CheckConstraint("ck_general_recommendation_settings_singleton_id", "id = 1");
                });

            migrationBuilder.Sql(
                """
                INSERT INTO general_recommendation_settings (
                    id,
                    median_period_years,
                    requires_vote_recalculation,
                    last_vote_recalculation_at,
                    last_vote_recalculation_period_years)
                SELECT
                    1,
                    COALESCE(settings.median_period_years, 5),
                    COALESCE(settings.requires_vote_recalculation, FALSE),
                    settings.last_vote_recalculation_at,
                    settings.last_vote_recalculation_period_years
                FROM (VALUES (1)) AS singleton(id)
                LEFT JOIN settings ON settings.id = singleton.id
                ON CONFLICT (id) DO NOTHING;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_settings_last_vote_recalculation_period_years",
                table: "settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_settings_median_period_years",
                table: "settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_settings_requires_vote_recalculation",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "last_vote_recalculation_at",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "last_vote_recalculation_period_years",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "median_period_years",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "requires_vote_recalculation",
                table: "settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_vote_recalculation_at",
                table: "settings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "last_vote_recalculation_period_years",
                table: "settings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "median_period_years",
                table: "settings",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<bool>(
                name: "requires_vote_recalculation",
                table: "settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_settings_last_vote_recalculation_period_years",
                table: "settings",
                sql: "last_vote_recalculation_period_years IS NULL OR last_vote_recalculation_period_years > 0");

            migrationBuilder.Sql(
                """
                UPDATE settings
                SET median_period_years = recommendation_settings.median_period_years,
                    requires_vote_recalculation = recommendation_settings.requires_vote_recalculation,
                    last_vote_recalculation_at = recommendation_settings.last_vote_recalculation_at,
                    last_vote_recalculation_period_years = recommendation_settings.last_vote_recalculation_period_years
                FROM general_recommendation_settings AS recommendation_settings
                WHERE settings.id = 1
                  AND recommendation_settings.id = 1;
                """);

            migrationBuilder.DropTable(
                name: "general_recommendation_settings");

            migrationBuilder.AddCheckConstraint(
                name: "ck_settings_median_period_years",
                table: "settings",
                sql: "median_period_years > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_settings_requires_vote_recalculation",
                table: "settings",
                sql: "requires_vote_recalculation IN (TRUE, FALSE)");
        }
    }
}
