using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendationFavoriteOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_favorite_movies_origin",
                table: "favorite_movies");

            migrationBuilder.AddColumn<bool>(
                name: "added_from_recommendations",
                table: "favorite_movies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_favorite_movies_origin",
                table: "favorite_movies",
                sql: "added_from_oscar OR added_from_catalog OR added_from_recommendations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_favorite_movies_origin",
                table: "favorite_movies");

                        migrationBuilder.Sql("""
                                UPDATE favorite_movies
                                SET added_from_catalog = TRUE
                                WHERE added_from_recommendations
                                    AND NOT added_from_oscar
                                    AND NOT added_from_catalog;
                                """);

            migrationBuilder.DropColumn(
                name: "added_from_recommendations",
                table: "favorite_movies");

            migrationBuilder.AddCheckConstraint(
                name: "ck_favorite_movies_origin",
                table: "favorite_movies",
                sql: "added_from_oscar OR added_from_catalog");
        }
    }
}
