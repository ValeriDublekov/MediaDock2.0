using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMovieRecommendationPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "updated_at",
                table: "settings",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

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

            migrationBuilder.AddColumn<double>(
                name: "normalized_vote_weight",
                table: "imdb_titles",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "category_key",
                table: "golden_globe_awards",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "award_other");

            migrationBuilder.CreateTable(
                name: "award_category_aliases",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    award_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    raw_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    category_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_award_category_aliases", x => x.id);
                    table.CheckConstraint("ck_award_category_aliases_names", "btrim(raw_name) <> '' AND btrim(category_key) <> ''");
                    table.CheckConstraint("ck_award_category_aliases_source", "award_source IN ('oscar', 'golden_globes')");
                });

            migrationBuilder.CreateTable(
                name: "recommendation_award_weights",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    award_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    category_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    win_points = table.Column<decimal>(type: "numeric", nullable: false),
                    nomination_points = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recommendation_award_weights", x => x.id);
                    table.CheckConstraint("ck_recommendation_award_weights_category_key", "btrim(category_key) <> ''");
                    table.CheckConstraint("ck_recommendation_award_weights_nomination_points", "nomination_points >= 0");
                    table.CheckConstraint("ck_recommendation_award_weights_source", "award_source IN ('oscar', 'golden_globes')");
                    table.CheckConstraint("ck_recommendation_award_weights_win_points", "win_points >= 0");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_settings_last_vote_recalculation_period_years",
                table: "settings",
                sql: "last_vote_recalculation_period_years IS NULL OR last_vote_recalculation_period_years > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_settings_median_period_years",
                table: "settings",
                sql: "median_period_years > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_settings_requires_vote_recalculation",
                table: "settings",
                sql: "requires_vote_recalculation IN (TRUE, FALSE)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_imdb_titles_normalized_vote_weight",
                table: "imdb_titles",
                sql: "normalized_vote_weight >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_golden_globe_awards_category_key",
                table: "golden_globe_awards",
                column: "category_key");

            migrationBuilder.CreateIndex(
                name: "ux_award_category_aliases_source_raw_name",
                table: "award_category_aliases",
                columns: new[] { "award_source", "raw_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_recommendation_award_weights_source_category",
                table: "recommendation_award_weights",
                columns: new[] { "award_source", "category_key" },
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO award_category_aliases (award_source, raw_name, category_key)
                VALUES
                    ('golden_globes', 'Best Motion Picture - Animated', 'best_motion_picture_animated'),
                    ('golden_globes', 'Best Motion Picture - Drama', 'best_motion_picture_drama'),
                    ('golden_globes', 'Best Motion Picture - Musical or Comedy', 'best_motion_picture_musical_or_comedy'),
                    ('golden_globes', 'Best Motion Picture – Non-English Language', 'foreign_film_foreign_language'),
                    ('golden_globes', 'Best Motion Picture - Non-English Language', 'foreign_film_foreign_language'),
                    ('golden_globes', 'Foreign Film - Foreign Language', 'foreign_film_foreign_language'),
                    ('golden_globes', 'Best Performance in Stand-Up Comedy on Television', 'best_performance_in_stand_up_comedy_on_television'),
                    ('golden_globes', 'Best Television Limited Series, Anthology Series, or Motion Picture Made for Television', 'best_television_limited_series_anthology_series_or_motion_picture_made_for_television'),
                    ('golden_globes', 'Best Television Series - Drama', 'best_television_series_drama'),
                    ('golden_globes', 'Best Television Series - Musical or Comedy', 'best_television_series_musical_or_comedy'),
                    ('golden_globes', 'Cinematic and Box Office Achievement', 'cinematic_and_box_office_achievement'),
                    ('golden_globes', 'Television Movie', 'television_movie'),
                    ('golden_globes', 'Television Special - Variety Or Musical', 'television_special_variety_or_musical'),
                    ('golden_globes', 'Best Screenplay - Motion Picture', 'best_screenplay_motion_picture')
                ON CONFLICT (award_source, raw_name) DO NOTHING;

                INSERT INTO award_category_aliases (award_source, raw_name, category_key)
                SELECT 'golden_globes', award.name, 'award_other'
                FROM golden_globe_awards AS award
                WHERE btrim(award.name) <> ''
                ON CONFLICT (award_source, raw_name) DO NOTHING;

                UPDATE golden_globe_awards AS award
                SET category_key = alias.category_key
                FROM award_category_aliases AS alias
                WHERE alias.award_source = 'golden_globes'
                  AND alias.raw_name = award.name;

                INSERT INTO recommendation_award_weights (
                    award_source, category_key, win_points, nomination_points)
                VALUES
                    ('oscar', 'BEST PICTURE', 3.0, 1.0),
                    ('oscar', 'DIRECTING', 3.0, 1.0),
                    ('oscar', 'WRITING (ORIGINAL SCREENPLAY)', 3.0, 1.0),
                    ('oscar', 'WRITING (ADAPTED SCREENPLAY)', 3.0, 1.0),
                    ('oscar', 'CINEMATOGRAPHY', 3.0, 1.0),
                    ('golden_globes', 'best_motion_picture_animated', 2.0, 0.5),
                    ('golden_globes', 'best_motion_picture_drama', 2.0, 0.5),
                    ('golden_globes', 'best_motion_picture_musical_or_comedy', 2.0, 0.5),
                    ('golden_globes', 'foreign_film_foreign_language', 2.0, 0.5),
                    ('golden_globes', 'best_performance_in_stand_up_comedy_on_television', 2.0, 0.5),
                    ('golden_globes', 'best_television_limited_series_anthology_series_or_motion_picture_made_for_television', 2.0, 0.5),
                    ('golden_globes', 'best_television_series_drama', 2.0, 0.5),
                    ('golden_globes', 'best_television_series_musical_or_comedy', 2.0, 0.5),
                    ('golden_globes', 'cinematic_and_box_office_achievement', 2.0, 0.5),
                    ('golden_globes', 'television_movie', 2.0, 0.5),
                    ('golden_globes', 'television_special_variety_or_musical', 2.0, 0.5),
                    ('golden_globes', 'best_screenplay_motion_picture', 2.0, 0.5),
                    ('oscar', 'award_other', 0.0, 0.0),
                    ('golden_globes', 'award_other', 0.0, 0.0)
                ON CONFLICT (award_source, category_key) DO NOTHING;

                INSERT INTO settings (
                    id,
                    excluded_genres,
                    excluded_countries,
                    min_movie_rating,
                    min_series_rating,
                    min_imdb_votes,
                    omdb_api_key,
                    omdb_daily_request_limit,
                    oscar_enrichment_max_films_per_run,
                    oscar_enrichment_max_requests_per_day,
                    median_period_years,
                    requires_vote_recalculation,
                    last_vote_recalculation_at,
                    last_vote_recalculation_period_years,
                    updated_at)
                VALUES (
                    1,
                    ARRAY[]::text[],
                    ARRAY[]::text[],
                    0,
                    0,
                    0,
                    NULL,
                    0,
                    0,
                    0,
                    5,
                    FALSE,
                    NULL,
                    NULL,
                    NULL)
                ON CONFLICT (id) DO NOTHING;

                UPDATE settings
                SET requires_vote_recalculation = EXISTS (
                    SELECT 1 FROM imdb_titles WHERE media_type = 'movie')
                WHERE id = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "award_category_aliases");

            migrationBuilder.DropTable(
                name: "recommendation_award_weights");

            migrationBuilder.DropCheckConstraint(
                name: "ck_settings_last_vote_recalculation_period_years",
                table: "settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_settings_median_period_years",
                table: "settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_settings_requires_vote_recalculation",
                table: "settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_imdb_titles_normalized_vote_weight",
                table: "imdb_titles");

            migrationBuilder.DropIndex(
                name: "ix_golden_globe_awards_category_key",
                table: "golden_globe_awards");

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

            migrationBuilder.DropColumn(
                name: "normalized_vote_weight",
                table: "imdb_titles");

            migrationBuilder.DropColumn(
                name: "category_key",
                table: "golden_globe_awards");

            migrationBuilder.Sql("UPDATE settings SET updated_at = now() WHERE updated_at IS NULL;");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "updated_at",
                table: "settings",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
