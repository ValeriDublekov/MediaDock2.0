using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserOwnershipToFavoritesAndRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_personal_ratings",
                table: "personal_ratings");

            migrationBuilder.DropPrimaryKey(
                name: "pk_favorite_movies",
                table: "favorite_movies");

            migrationBuilder.AddColumn<long>(
                name: "user_id",
                table: "personal_ratings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "user_id",
                table: "favorite_movies",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    user_count bigint;
                    sole_user_id bigint;
                BEGIN
                    SELECT COUNT(*) INTO user_count FROM users;

                    IF user_count = 0 THEN
                        DELETE FROM favorite_movies;
                        DELETE FROM personal_ratings;
                    ELSIF user_count = 1 THEN
                        SELECT id INTO sole_user_id FROM users;
                        UPDATE favorite_movies SET user_id = sole_user_id;
                        UPDATE personal_ratings SET user_id = sole_user_id;
                    ELSE
                        INSERT INTO favorite_movies (
                            user_id,
                            title_id,
                            to_watch,
                            to_download,
                            added_from_oscar,
                            added_from_catalog,
                            created_at,
                            updated_at)
                        SELECT users.id,
                               legacy_favorite.title_id,
                               legacy_favorite.to_watch,
                               legacy_favorite.to_download,
                               legacy_favorite.added_from_oscar,
                               legacy_favorite.added_from_catalog,
                               legacy_favorite.created_at,
                               legacy_favorite.updated_at
                        FROM users
                        CROSS JOIN favorite_movies AS legacy_favorite
                        WHERE legacy_favorite.user_id IS NULL;
                        DELETE FROM favorite_movies WHERE user_id IS NULL;

                        INSERT INTO personal_ratings (user_id, imdb_id, rating, updated_at)
                        SELECT users.id,
                               legacy_rating.imdb_id,
                               legacy_rating.rating,
                               legacy_rating.updated_at
                        FROM users
                        CROSS JOIN personal_ratings AS legacy_rating
                        WHERE legacy_rating.user_id IS NULL;
                        DELETE FROM personal_ratings WHERE user_id IS NULL;
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<long>(
                name: "user_id",
                table: "personal_ratings",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "user_id",
                table: "favorite_movies",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "pk_personal_ratings",
                table: "personal_ratings",
                columns: new[] { "user_id", "imdb_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_favorite_movies",
                table: "favorite_movies",
                columns: new[] { "user_id", "title_id" });

            migrationBuilder.CreateIndex(
                name: "IX_favorite_movies_title_id",
                table: "favorite_movies",
                column: "title_id");

            migrationBuilder.AddForeignKey(
                name: "fk_favorite_movies_users_user_id",
                table: "favorite_movies",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_personal_ratings_users_user_id",
                table: "personal_ratings",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF (SELECT COUNT(*) FROM users) > 1 THEN
                        RAISE EXCEPTION 'Cannot automatically restore ownerless favorites and ratings when multiple users exist';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_favorite_movies_users_user_id",
                table: "favorite_movies");

            migrationBuilder.DropForeignKey(
                name: "fk_personal_ratings_users_user_id",
                table: "personal_ratings");

            migrationBuilder.DropPrimaryKey(
                name: "pk_personal_ratings",
                table: "personal_ratings");

            migrationBuilder.DropPrimaryKey(
                name: "pk_favorite_movies",
                table: "favorite_movies");

            migrationBuilder.DropIndex(
                name: "IX_favorite_movies_title_id",
                table: "favorite_movies");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "personal_ratings");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "favorite_movies");

            migrationBuilder.AddPrimaryKey(
                name: "pk_personal_ratings",
                table: "personal_ratings",
                column: "imdb_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_favorite_movies",
                table: "favorite_movies",
                column: "title_id");
        }
    }
}
