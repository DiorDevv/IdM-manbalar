using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Idm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Mover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "branch",
                table: "role_rules",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "mover_due_at",
                table: "identity_roles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "branch",
                table: "identities",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_identity_roles_mover_due_at",
                table: "identity_roles",
                column: "mover_due_at",
                filter: "mover_due_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_identity_roles_mover_due_at",
                table: "identity_roles");

            migrationBuilder.DropColumn(
                name: "branch",
                table: "role_rules");

            migrationBuilder.DropColumn(
                name: "mover_due_at",
                table: "identity_roles");

            migrationBuilder.DropColumn(
                name: "branch",
                table: "identities");
        }
    }
}
