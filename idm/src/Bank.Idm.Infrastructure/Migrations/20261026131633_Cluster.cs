using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Idm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Cluster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "runner_id",
                table: "connector_runs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cluster_nodes",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    host = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_leader = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cluster_nodes", x => x.id);
                });

            // Noyob indeksdan oldin: avvalgi versiyadan qolgan tugallanmagan sinxronlashlar yopiladi (migratsiya server to'xtaganda qo'llanadi)
            migrationBuilder.Sql("UPDATE connector_runs SET status = 'Failed', finished_at = now(), error = 'Yangilanish paytida yakunlanmagan' WHERE status = 'Running';");

            migrationBuilder.CreateIndex(
                name: "ix_connector_runs_one_running",
                table: "connector_runs",
                column: "application_id",
                unique: true,
                filter: "status = 'Running'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cluster_nodes");

            migrationBuilder.DropIndex(
                name: "ix_connector_runs_one_running",
                table: "connector_runs");

            migrationBuilder.DropColumn(
                name: "runner_id",
                table: "connector_runs");
        }
    }
}
