using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImdbTitleLookupIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY ix_imdb_titles_title_media_type_year ON imdb_titles (title, media_type, year)",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS ix_imdb_titles_title_media_type_year",
                suppressTransaction: true);
        }
    }
}
