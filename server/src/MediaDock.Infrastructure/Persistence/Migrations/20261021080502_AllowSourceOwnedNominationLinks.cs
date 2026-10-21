using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowSourceOwnedNominationLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "oscar_film_id",
                table: "oscar_nominations",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "group_id",
                table: "golden_globe_nominations",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM oscar_nominations WHERE oscar_film_id IS NULL)
                       OR EXISTS (SELECT 1 FROM golden_globe_nominations WHERE group_id IS NULL) THEN
                        RAISE EXCEPTION 'Cannot restore required legacy nomination links while source-owned nominations are unlinked';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<long>(
                name: "oscar_film_id",
                table: "oscar_nominations",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "group_id",
                table: "golden_globe_nominations",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
