using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Idm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountOrgAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "branch",
                table: "accounts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "department",
                table: "accounts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "job_title",
                table: "accounts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "manager_dn",
                table: "accounts",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "org_synced",
                table: "accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "branch",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "department",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "job_title",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "manager_dn",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "org_synced",
                table: "accounts");
        }
    }
}
