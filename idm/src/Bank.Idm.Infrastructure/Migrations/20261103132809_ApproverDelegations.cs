using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Idm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ApproverDelegations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "approver_delegations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    delegator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delegate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approver_delegations", x => x.id);
                    table.ForeignKey(
                        name: "fk_approver_delegations_identities_delegate_id",
                        column: x => x.delegate_id,
                        principalTable: "identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_approver_delegations_identities_delegator_id",
                        column: x => x.delegator_id,
                        principalTable: "identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_approver_delegations_delegate_id_valid_to",
                table: "approver_delegations",
                columns: new[] { "delegate_id", "valid_to" });

            migrationBuilder.CreateIndex(
                name: "ix_approver_delegations_delegator_id_valid_to",
                table: "approver_delegations",
                columns: new[] { "delegator_id", "valid_to" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approver_delegations");
        }
    }
}
