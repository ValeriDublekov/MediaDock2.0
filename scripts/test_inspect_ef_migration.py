import contextlib
import io
import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from scripts.inspect_ef_migration import analyze, main, parse_snapshot, render_text, tokenize


ROOT = Path(__file__).resolve().parents[1]
MIGRATIONS = ROOT / "server/src/MediaDock.Infrastructure/Persistence/Migrations"
MIGRATION = MIGRATIONS / "20261021111712_RemoveLegacyTitleIdentity.cs"
SNAPSHOT = MIGRATIONS / "MediaDockDbContextModelSnapshot.cs"


class EfCSharpScannerTests(unittest.TestCase):
    def test_balances_nested_calls_and_ignores_comments_and_raw_strings(self) -> None:
        source = '''
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                // migrationBuilder.DropTable(name: "commented");
                migrationBuilder.Sql($$"""
                    DO $$ BEGIN
                        RAISE NOTICE 'text with ), { and migrationBuilder.DropTable('; 
                    END $$;
                """);
                migrationBuilder.DropColumn(
                    name: "group_id",
                    table: "golden_globe_nominations");
            }
        '''

        tokens = tokenize(source)
        self.assertTrue(any(token.kind == "STRING" and "migrationBuilder.DropTable" in token.value for token in tokens))
        report = analyze_source(source)
        self.assertEqual([operation["operation"] for operation in report["Up"]], ["Sql", "DropColumn"])
        self.assertEqual(report["Up"][1]["column"], "group_id")

    def test_snapshot_extracts_only_requested_entity_contract_data(self) -> None:
        snapshot = '''
            modelBuilder.Entity("Demo.Favorite", b =>
            {
                b.Property<long>("UserId").HasColumnName("user_id");
                b.Property<string>("ImdbId").HasColumnName("imdb_id");
                b.PrimitiveCollection<string[]>("Genres").HasColumnName("genres");
                b.ToTable("favorite_movies");
                b.HasIndex("ImdbId").HasDatabaseName("ix_favorite_movies_imdb_id");
                b.HasOne("Demo.User", "User").WithMany().HasForeignKey("UserId")
                    .HasConstraintName("fk_favorite_movies_users_user_id");
            });
        '''

        model = parse_snapshot(snapshot)

        self.assertEqual(model["entities"][0]["table"], "favorite_movies")
        self.assertEqual(model["entities"][0]["properties"], [
            {"property": "UserId", "column": "user_id"},
            {"property": "ImdbId", "column": "imdb_id"},
            {"property": "Genres", "column": "genres"},
        ])
        self.assertEqual(model["entities"][0]["indexes"], ["ix_favorite_movies_imdb_id"])
        self.assertEqual(model["tables"]["favorite_movies"]["foreignKeys"], {"fk_favorite_movies_users_user_id"})


def analyze_source(source: str) -> dict[str, list[dict]]:
    from scripts.inspect_ef_migration import _extract_operations

    return _extract_operations(source)


class EfMigrationInspectionTests(unittest.TestCase):
    def test_remove_legacy_migration_has_expected_operations_and_sql(self) -> None:
        report = analyze(MIGRATION, SNAPSHOT, [])
        up = report["operations"]["Up"]

        self.assertEqual(sum(operation["operation"] == "DropTable" for operation in up), 5)
        self.assertEqual(
            {operation["column"] for operation in up if operation["operation"] == "DropColumn"},
            {"oscar_film_id", "group_id"},
        )
        self.assertEqual(sum(operation["operation"] == "DropForeignKey" for operation in up), 2)
        self.assertEqual(sum(operation["operation"] == "DropIndex" for operation in up), 2)
        self.assertEqual(report["rawSqlCount"], 1)
        self.assertNotIn("sql", report["rawSql"][0])
        self.assertLessEqual(len(report["rawSql"][0]["excerpt"]), 243)
        self.assertNotIn("sql", up[0])
        self.assertFalse(any(operation.get("columns") for operation in report["operations"]["Down"]
                     if operation["operation"] == "CreateTable"))
        rendered = render_text(report)
        self.assertNotIn("CREATE TABLE", rendered)
        self.assertNotIn("FROM legacy_favorite_movies", rendered)
        self.assertIn("DO $$ DECLARE fallback_metadata_title_count", rendered)
        self.assertFalse(report["suspicious"])

    def test_drop_of_fk_retained_by_active_snapshot_is_flagged(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            migration_source = MIGRATION.read_text(encoding="utf-8")
            injected_operation = '''migrationBuilder.DropForeignKey(
                name: "fk_favorite_movies_users_user_id",
                table: "favorite_movies");

            migrationBuilder.Sql('''
            self.assertIn("migrationBuilder.Sql(", migration_source)
            modified_source = migration_source.replace("migrationBuilder.Sql(", injected_operation, 1)
            migration = Path(directory) / MIGRATION.name
            migration.write_text(modified_source, encoding="utf-8")

            report = analyze(migration, SNAPSHOT, [])

        self.assertEqual(len(report["suspicious"]), 1)
        self.assertIn("remains on active table 'favorite_movies'", report["suspicious"][0]["message"])

    def test_entity_filter_and_full_sql_are_opt_in(self) -> None:
        report = analyze(MIGRATION, SNAPSHOT, ["ImdbFavoriteMovie"], full_sql=True)

        self.assertEqual(len(report["entities"]), 1)
        self.assertEqual(report["entities"][0]["table"], "favorite_movies")
        self.assertIn("sql", report["rawSql"][0])

    def test_designer_file_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            designer = Path(directory) / "20260101000000_Example.Designer.cs"
            designer.write_text("class Example {}", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "not a historical"):
                analyze(designer, None, [])

    def test_json_output_is_written_under_test_tmp(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            migration_dir = root / "migrations"
            migration_dir.mkdir()
            migration = migration_dir / "Example.cs"
            migration.write_text("class Example {}", encoding="utf-8")
            output = io.StringIO()
            with mock.patch("scripts.inspect_ef_migration.ROOT", root):
                with contextlib.redirect_stdout(output):
                    self.assertEqual(main([str(migration), "--format", "json", "--output", "report.json"]), 0)

            result = json.loads((root / ".test-tmp/report.json").read_text(encoding="utf-8"))
            self.assertEqual(result["operations"], {"Up": [], "Down": []})
            self.assertIn(".test-tmp/report.json", output.getvalue())


if __name__ == "__main__":
    unittest.main()