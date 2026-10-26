using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Idm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HrSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "identities_missing",
                table: "connector_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "identities_terminated",
                table: "connector_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "identities_updated",
                table: "connector_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "identity_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    connector_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    details = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_identity_events_identities_identity_id",
                        column: x => x.identity_id,
                        principalTable: "identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_identity_events_identity_id_occurred_at",
                table: "identity_events",
                columns: new[] { "identity_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_identity_events_occurred_at",
                table: "identity_events",
                column: "occurred_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_events");

            migrationBuilder.DropColumn(
                name: "identities_missing",
                table: "connector_runs");

            migrationBuilder.DropColumn(
                name: "identities_terminated",
                table: "connector_runs");

            migrationBuilder.DropColumn(
                name: "identities_updated",
                table: "connector_runs");
        }
    }
}
