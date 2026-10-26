using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Idm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccessRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_access_requests_beneficiary_id",
                table: "access_requests");

            migrationBuilder.AddColumn<string>(
                name: "decided_by",
                table: "approvals",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decided_by_name",
                table: "approvals",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "approvals",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<string>(
                name: "sod_conflicts",
                table: "access_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "access_requests",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "ix_access_requests_beneficiary_id_status",
                table: "access_requests",
                columns: new[] { "beneficiary_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_access_requests_beneficiary_id_status",
                table: "access_requests");

            migrationBuilder.DropColumn(
                name: "decided_by",
                table: "approvals");

            migrationBuilder.DropColumn(
                name: "decided_by_name",
                table: "approvals");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "approvals");

            migrationBuilder.DropColumn(
                name: "sod_conflicts",
                table: "access_requests");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "access_requests");

            migrationBuilder.CreateIndex(
                name: "ix_access_requests_beneficiary_id",
                table: "access_requests",
                column: "beneficiary_id");
        }
    }
}
