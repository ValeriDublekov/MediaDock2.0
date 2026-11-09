using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOmdbQuotaRecoveryTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_provider_error_at",
                table: "omdb_daily_usage",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_provider_request_at",
                table: "omdb_daily_usage",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_provider_success_at",
                table: "omdb_daily_usage",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "provider_quota_exceeded_at",
                table: "omdb_daily_usage",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "quota_recovery_attempted_at",
                table: "omdb_daily_usage",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "quota_recovery_succeeded_at",
                table: "omdb_daily_usage",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_provider_error_at",
                table: "omdb_daily_usage");

            migrationBuilder.DropColumn(
                name: "last_provider_request_at",
                table: "omdb_daily_usage");

            migrationBuilder.DropColumn(
                name: "last_provider_success_at",
                table: "omdb_daily_usage");

            migrationBuilder.DropColumn(
                name: "provider_quota_exceeded_at",
                table: "omdb_daily_usage");

            migrationBuilder.DropColumn(
                name: "quota_recovery_attempted_at",
                table: "omdb_daily_usage");

            migrationBuilder.DropColumn(
                name: "quota_recovery_succeeded_at",
                table: "omdb_daily_usage");
        }
    }
}
