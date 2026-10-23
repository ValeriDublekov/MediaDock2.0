using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBayesianRecommendationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_override",
                table: "recommendation_award_weights",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "bayesian_default_c",
                table: "general_recommendation_settings",
                type: "numeric",
                nullable: false,
                defaultValue: 5.5m);

            migrationBuilder.AddColumn<decimal>(
                name: "bayesian_m_ratio",
                table: "general_recommendation_settings",
                type: "numeric",
                nullable: false,
                defaultValue: 0.25m);

            migrationBuilder.AddColumn<decimal>(
                name: "gg_nom_points",
                table: "general_recommendation_settings",
                type: "numeric",
                nullable: false,
                defaultValue: 0.5m);

            migrationBuilder.AddColumn<decimal>(
                name: "gg_win_points",
                table: "general_recommendation_settings",
                type: "numeric",
                nullable: false,
                defaultValue: 2m);

            migrationBuilder.AddColumn<decimal>(
                name: "oscar_nom_points",
                table: "general_recommendation_settings",
                type: "numeric",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "oscar_win_points",
                table: "general_recommendation_settings",
                type: "numeric",
                nullable: false,
                defaultValue: 3m);

            migrationBuilder.Sql(
                """
                UPDATE recommendation_award_weights
                SET is_override = TRUE
                WHERE (category_key = 'award_other'
                       AND ((award_source = 'oscar' AND (win_points <> 0 OR nomination_points <> 0))
                            OR (award_source = 'golden_globes' AND (win_points <> 0 OR nomination_points <> 0))))
                   OR (category_key <> 'award_other'
                       AND ((award_source = 'oscar' AND (win_points <> 3 OR nomination_points <> 1))
                            OR (award_source = 'golden_globes' AND (win_points <> 2 OR nomination_points <> 0.5))));
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_general_recommendation_settings_bayesian_default_c",
                table: "general_recommendation_settings",
                sql: "bayesian_default_c >= 0 AND bayesian_default_c <= 10");

                        migrationBuilder.Sql(
                                """
                                UPDATE general_recommendation_settings
                                SET requires_vote_recalculation = TRUE
                                WHERE id = 1
                                    AND EXISTS (SELECT 1 FROM imdb_titles WHERE media_type = 'movie');
                                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_general_recommendation_settings_bayesian_m_ratio",
                table: "general_recommendation_settings",
                sql: "bayesian_m_ratio > 0 AND bayesian_m_ratio <= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_general_recommendation_settings_gg_nom_points",
                table: "general_recommendation_settings",
                sql: "gg_nom_points >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_general_recommendation_settings_gg_win_points",
                table: "general_recommendation_settings",
                sql: "gg_win_points >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_general_recommendation_settings_oscar_nom_points",
                table: "general_recommendation_settings",
                sql: "oscar_nom_points >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_general_recommendation_settings_oscar_win_points",
                table: "general_recommendation_settings",
                sql: "oscar_win_points >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_general_recommendation_settings_bayesian_default_c",
                table: "general_recommendation_settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_general_recommendation_settings_bayesian_m_ratio",
                table: "general_recommendation_settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_general_recommendation_settings_gg_nom_points",
                table: "general_recommendation_settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_general_recommendation_settings_gg_win_points",
                table: "general_recommendation_settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_general_recommendation_settings_oscar_nom_points",
                table: "general_recommendation_settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_general_recommendation_settings_oscar_win_points",
                table: "general_recommendation_settings");

            migrationBuilder.DropColumn(
                name: "is_override",
                table: "recommendation_award_weights");

            migrationBuilder.DropColumn(
                name: "bayesian_default_c",
                table: "general_recommendation_settings");

            migrationBuilder.DropColumn(
                name: "bayesian_m_ratio",
                table: "general_recommendation_settings");

            migrationBuilder.DropColumn(
                name: "gg_nom_points",
                table: "general_recommendation_settings");

            migrationBuilder.DropColumn(
                name: "gg_win_points",
                table: "general_recommendation_settings");

            migrationBuilder.DropColumn(
                name: "oscar_nom_points",
                table: "general_recommendation_settings");

            migrationBuilder.DropColumn(
                name: "oscar_win_points",
                table: "general_recommendation_settings");
        }
    }
}
