using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Idm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RoleCertification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "certified_at",
                table: "roles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "certified_by",
                table: "roles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "certified_by_name",
                table: "roles",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "certified_at",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "certified_by",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "certified_by_name",
                table: "roles");
        }
    }
}
