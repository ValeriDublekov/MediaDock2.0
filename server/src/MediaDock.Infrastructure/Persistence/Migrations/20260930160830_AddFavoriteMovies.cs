using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFavoriteMovies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "favorite_movies",
                columns: table => new
                {
                    title_id = table.Column<long>(type: "bigint", nullable: false),
                    to_watch = table.Column<bool>(type: "boolean", nullable: false),
                    to_download = table.Column<bool>(type: "boolean", nullable: false),
                    added_from_oscar = table.Column<bool>(type: "boolean", nullable: false),
                    added_from_catalog = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_favorite_movies", x => x.title_id);
                    table.CheckConstraint("ck_favorite_movies_origin", "added_from_oscar OR added_from_catalog");
                    table.ForeignKey(
                        name: "fk_favorite_movies_titles_title_id",
                        column: x => x.title_id,
                        principalTable: "titles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "favorite_movies");
        }
    }
}
