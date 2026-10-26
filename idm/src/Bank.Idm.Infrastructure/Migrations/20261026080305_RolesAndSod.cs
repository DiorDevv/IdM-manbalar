using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Idm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RolesAndSod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_role_entitlements_entitlements_entitlement_id",
                table: "role_entitlements");

            migrationBuilder.DropIndex(
                name: "ix_identity_roles_identity_id_role_id",
                table: "identity_roles");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "roles",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                table: "roles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateTable(
                name: "role_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    job_title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_rules_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sod_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    risk = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sod_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sod_rule_entitlements",
                columns: table => new
                {
                    sod_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entitlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    side = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sod_rule_entitlements", x => new { x.sod_rule_id, x.entitlement_id });
                    table.ForeignKey(
                        name: "fk_sod_rule_entitlements_entitlements_entitlement_id",
                        column: x => x.entitlement_id,
                        principalTable: "entitlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sod_rule_entitlements_sod_rules_sod_rule_id",
                        column: x => x.sod_rule_id,
                        principalTable: "sod_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_identity_roles_identity_id_role_id_source",
                table: "identity_roles",
                columns: new[] { "identity_id", "role_id", "source" },
                unique: true,
                filter: "valid_to IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_role_rules_role_id",
                table: "role_rules",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_sod_rule_entitlements_entitlement_id",
                table: "sod_rule_entitlements",
                column: "entitlement_id");

            migrationBuilder.CreateIndex(
                name: "ix_sod_rules_code",
                table: "sod_rules",
                column: "code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_role_entitlements_entitlements_entitlement_id",
                table: "role_entitlements",
                column: "entitlement_id",
                principalTable: "entitlements",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_role_entitlements_entitlements_entitlement_id",
                table: "role_entitlements");

            migrationBuilder.DropTable(
                name: "role_rules");

            migrationBuilder.DropTable(
                name: "sod_rule_entitlements");

            migrationBuilder.DropTable(
                name: "sod_rules");

            migrationBuilder.DropIndex(
                name: "ix_identity_roles_identity_id_role_id_source",
                table: "identity_roles");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "roles");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "roles",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_identity_roles_identity_id_role_id",
                table: "identity_roles",
                columns: new[] { "identity_id", "role_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_role_entitlements_entitlements_entitlement_id",
                table: "role_entitlements",
                column: "entitlement_id",
                principalTable: "entitlements",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
