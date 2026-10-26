using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Idm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccessReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "access_review_campaigns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    department = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    privileged_only = table.Column<bool>(type: "boolean", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_review_campaigns", x => x.id);
                    table.ForeignKey(
                        name: "fk_access_review_campaigns_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "access_review_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    identity_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entitlement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    application_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    subtitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_privileged = table.Column<bool>(type: "boolean", nullable: false),
                    decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    decided_by_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    remediated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_review_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_access_review_items_access_review_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "access_review_campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_access_review_items_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_review_items_entitlements_entitlement_id",
                        column: x => x.entitlement_id,
                        principalTable: "entitlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_review_items_identities_identity_id",
                        column: x => x.identity_id,
                        principalTable: "identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_review_items_identities_reviewer_id",
                        column: x => x.reviewer_id,
                        principalTable: "identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_review_items_identity_roles_identity_role_id",
                        column: x => x.identity_role_id,
                        principalTable: "identity_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_review_campaigns_application_id",
                table: "access_review_campaigns",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_review_campaigns_status_created_at",
                table: "access_review_campaigns",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_access_review_items_account_id",
                table: "access_review_items",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_review_items_campaign_id_decision",
                table: "access_review_items",
                columns: new[] { "campaign_id", "decision" });

            migrationBuilder.CreateIndex(
                name: "ix_access_review_items_decision_remediated_at",
                table: "access_review_items",
                columns: new[] { "decision", "remediated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_access_review_items_entitlement_id",
                table: "access_review_items",
                column: "entitlement_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_review_items_identity_id",
                table: "access_review_items",
                column: "identity_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_review_items_identity_role_id",
                table: "access_review_items",
                column: "identity_role_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_review_items_reviewer_id_decision",
                table: "access_review_items",
                columns: new[] { "reviewer_id", "decision" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_review_items");

            migrationBuilder.DropTable(
                name: "access_review_campaigns");
        }
    }
}
