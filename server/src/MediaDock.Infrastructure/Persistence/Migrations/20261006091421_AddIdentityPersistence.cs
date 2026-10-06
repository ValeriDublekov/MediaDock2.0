using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations;

public partial class AddIdentityPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                given_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                family_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_users", entity => entity.id);
                table.CheckConstraint("ck_users_normalized_email", "normalized_email <> '' AND normalized_email = lower(btrim(normalized_email))");
                table.CheckConstraint("ck_users_given_name", "btrim(given_name) <> ''");
                table.CheckConstraint("ck_users_family_name", "btrim(family_name) <> ''");
                table.CheckConstraint("ck_users_status", "status IN ('pending', 'active', 'deactivated')");
                table.CheckConstraint("ck_users_role", "role IS NULL OR role IN ('admin', 'user')");
                table.CheckConstraint(
                    "ck_users_status_role",
                    "(status = 'pending' AND role IS NULL) OR (status IN ('active', 'deactivated') AND role IS NOT NULL)");
            });

        migrationBuilder.CreateTable(
            name: "external_identities",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                user_id = table.Column<long>(type: "bigint", nullable: false),
                issuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                subject = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_external_identities", entity => entity.id);
                table.CheckConstraint("ck_external_identities_issuer_subject", "btrim(issuer) <> '' AND btrim(subject) <> ''");
                table.ForeignKey(
                    name: "fk_external_identities_users_user_id",
                    column: entity => entity.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "registration_requests",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                user_id = table.Column<long>(type: "bigint", nullable: false),
                requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                decided_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_registration_requests", entity => entity.id);
                table.CheckConstraint(
                    "ck_registration_requests_status",
                    "status IN ('pending', 'approved', 'rejected')");
                table.CheckConstraint(
                    "ck_registration_requests_decision",
                    "(status = 'pending' AND decided_at IS NULL AND decided_by IS NULL) OR " +
                    "(status IN ('approved', 'rejected') AND decided_at IS NOT NULL AND decided_by IS NOT NULL AND btrim(decided_by) <> '')");
                table.ForeignKey(
                    name: "fk_registration_requests_users_user_id",
                    column: entity => entity.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_external_identities_user_id",
            table: "external_identities",
            column: "user_id");
        migrationBuilder.CreateIndex(
            name: "ux_external_identities_issuer_subject",
            table: "external_identities",
            columns: new[] { "issuer", "subject" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ix_registration_requests_user_id_requested_at",
            table: "registration_requests",
            columns: new[] { "user_id", "requested_at" });
        migrationBuilder.CreateIndex(
            name: "ux_registration_requests_pending_user_id",
            table: "registration_requests",
            column: "user_id",
            unique: true,
            filter: "status = 'pending'");
        migrationBuilder.CreateIndex(
            name: "ux_users_normalized_email",
            table: "users",
            column: "normalized_email",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "external_identities");
        migrationBuilder.DropTable(name: "registration_requests");
        migrationBuilder.DropTable(name: "users");
    }
}