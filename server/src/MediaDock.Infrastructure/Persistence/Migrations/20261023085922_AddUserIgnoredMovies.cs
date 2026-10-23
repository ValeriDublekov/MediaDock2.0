using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIgnoredMovies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_ignored_movies",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    imdb_id = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_ignored_movies", x => new { x.user_id, x.imdb_id });
                    table.ForeignKey(
                        name: "fk_user_ignored_movies_imdb_titles_imdb_id",
                        column: x => x.imdb_id,
                        principalTable: "imdb_titles",
                        principalColumn: "imdb_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_ignored_movies_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_ignored_movies_imdb_id",
                table: "user_ignored_movies",
                column: "imdb_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_ignored_movies_user_id_created_at",
                table: "user_ignored_movies",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_ignored_movies");
        }
    }
}
