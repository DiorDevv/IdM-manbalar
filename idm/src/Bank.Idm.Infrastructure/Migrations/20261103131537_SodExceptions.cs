using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Idm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SodExceptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sod_exceptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sod_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    compensating_control = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    revoked_by_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sod_exceptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_sod_exceptions_identities_identity_id",
                        column: x => x.identity_id,
                        principalTable: "identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sod_exceptions_sod_rules_sod_rule_id",
                        column: x => x.sod_rule_id,
                        principalTable: "sod_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sod_exceptions_identity_id",
                table: "sod_exceptions",
                column: "identity_id");

            migrationBuilder.CreateIndex(
                name: "ix_sod_exceptions_sod_rule_id_identity_id",
                table: "sod_exceptions",
                columns: new[] { "sod_rule_id", "identity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sod_exceptions_valid_to",
                table: "sod_exceptions",
                column: "valid_to");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sod_exceptions");
        }
    }
}
