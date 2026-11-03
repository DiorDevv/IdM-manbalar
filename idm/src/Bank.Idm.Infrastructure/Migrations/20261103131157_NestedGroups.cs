using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Idm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NestedGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "via_entitlement_id",
                table: "account_entitlements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_entitlements_via_entitlement_id",
                table: "account_entitlements",
                column: "via_entitlement_id");

            migrationBuilder.AddForeignKey(
                name: "fk_account_entitlements_entitlements_via_entitlement_id",
                table: "account_entitlements",
                column: "via_entitlement_id",
                principalTable: "entitlements",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_account_entitlements_entitlements_via_entitlement_id",
                table: "account_entitlements");

            migrationBuilder.DropIndex(
                name: "ix_account_entitlements_via_entitlement_id",
                table: "account_entitlements");

            migrationBuilder.DropColumn(
                name: "via_entitlement_id",
                table: "account_entitlements");
        }
    }
}
