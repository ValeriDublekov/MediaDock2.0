using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceOscarFilmTitleIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM oscar_films AS film
                        INNER JOIN titles AS title ON title.id = film.title_id
                        WHERE film.imdb_id IS NOT NULL
                            AND film.imdb_id IS DISTINCT FROM title.imdb_id
                    ) THEN
                        RAISE EXCEPTION 'Cannot enforce Oscar film IMDb identity: a non-null source IMDb ID differs from the linked title.';
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_titles_id_imdb_id",
                table: "titles",
                columns: new[] { "id", "imdb_id" },
                unique: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE oscar_films
                ADD CONSTRAINT fk_oscar_films_titles_title_id_imdb_id
                    FOREIGN KEY (title_id, imdb_id)
                    REFERENCES titles (id, imdb_id)
                    DEFERRABLE INITIALLY DEFERRED;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_oscar_films_titles_title_id_imdb_id",
                table: "oscar_films");

            migrationBuilder.DropIndex(
                name: "ux_titles_id_imdb_id",
                table: "titles");
        }
    }
}
