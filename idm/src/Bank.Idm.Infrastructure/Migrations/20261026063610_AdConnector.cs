using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Idm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdConnector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "entitlements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_synced_at",
                table: "entitlements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "accounts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "distinguished_name",
                table: "accounts",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "accounts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "employee_number",
                table: "accounts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "connector_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    triggered_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accounts_read = table.Column<int>(type: "integer", nullable: false),
                    groups_read = table.Column<int>(type: "integer", nullable: false),
                    accounts_created = table.Column<int>(type: "integer", nullable: false),
                    accounts_updated = table.Column<int>(type: "integer", nullable: false),
                    accounts_disabled = table.Column<int>(type: "integer", nullable: false),
                    accounts_deleted = table.Column<int>(type: "integer", nullable: false),
                    memberships_added = table.Column<int>(type: "integer", nullable: false),
                    memberships_removed = table.Column<int>(type: "integer", nullable: false),
                    identities_created = table.Column<int>(type: "integer", nullable: false),
                    orphan_accounts = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connector_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_connector_runs_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_employee_number",
                table: "accounts",
                column: "employee_number");

            migrationBuilder.CreateIndex(
                name: "ix_connector_runs_application_id_started_at",
                table: "connector_runs",
                columns: new[] { "application_id", "started_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "connector_runs");

            migrationBuilder.DropIndex(
                name: "ix_accounts_employee_number",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "entitlements");

            migrationBuilder.DropColumn(
                name: "last_synced_at",
                table: "entitlements");

            migrationBuilder.DropColumn(
                name: "display_name",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "distinguished_name",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "email",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "employee_number",
                table: "accounts");
        }
    }
}
