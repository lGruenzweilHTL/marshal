using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProactiveLearning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "input_embedding",
                table: "runs",
                type: "BLOB",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "maintenance_state",
                columns: table => new
                {
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    item_id = table.Column<string>(type: "TEXT", nullable: false),
                    baseline = table.Column<double>(type: "REAL", nullable: false),
                    last_done_at = table.Column<long>(type: "INTEGER", nullable: true),
                    last_value = table.Column<double>(type: "REAL", nullable: false),
                    unit = table.Column<string>(type: "TEXT", nullable: true),
                    last_read_at = table.Column<long>(type: "INTEGER", nullable: false),
                    notified_soon = table.Column<bool>(type: "INTEGER", nullable: false),
                    notified_due = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_state", x => new { x.owner_id, x.item_id });
                    table.ForeignKey(
                        name: "fk_maintenance_state_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "skill_proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    diff = table.Column<string>(type: "TEXT", nullable: true),
                    source_run_ids = table.Column<string>(type: "TEXT", nullable: false),
                    sequence_hash = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    decided_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_skill_proposals", x => x.id);
                    table.ForeignKey(
                        name: "fk_skill_proposals_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "suggestion_detector_stats",
                columns: table => new
                {
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    detector = table.Column<string>(type: "TEXT", nullable: false),
                    sent = table.Column<int>(type: "INTEGER", nullable: false),
                    accepted = table.Column<int>(type: "INTEGER", nullable: false),
                    dismissed = table.Column<int>(type: "INTEGER", nullable: false),
                    muted_until = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suggestion_detector_stats", x => new { x.owner_id, x.detector });
                    table.ForeignKey(
                        name: "fk_suggestion_detector_stats_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "suggestions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    detector = table.Column<string>(type: "TEXT", nullable: false),
                    dedupe_key = table.Column<string>(type: "TEXT", nullable: false),
                    text = table.Column<string>(type: "TEXT", nullable: false),
                    facts_json = table.Column<string>(type: "TEXT", nullable: false),
                    delivery = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    nonce = table.Column<string>(type: "TEXT", nullable: false),
                    pending_action_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    action_summary = table.Column<string>(type: "TEXT", nullable: true),
                    snippet = table.Column<string>(type: "TEXT", nullable: true),
                    notification_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    expires_at = table.Column<long>(type: "INTEGER", nullable: false),
                    decided_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suggestions", x => x.id);
                    table.ForeignKey(
                        name: "fk_suggestions_pending_actions_pending_action_id",
                        column: x => x.pending_action_id,
                        principalTable: "pending_actions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_suggestions_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_skill_proposals_owner_id_sequence_hash",
                table: "skill_proposals",
                columns: new[] { "owner_id", "sequence_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_skill_proposals_owner_id_status",
                table: "skill_proposals",
                columns: new[] { "owner_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_suggestions_nonce",
                table: "suggestions",
                column: "nonce",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_suggestions_owner_id_detector_dedupe_key_created_at",
                table: "suggestions",
                columns: new[] { "owner_id", "detector", "dedupe_key", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_suggestions_pending_action_id",
                table: "suggestions",
                column: "pending_action_id");

            migrationBuilder.CreateIndex(
                name: "ix_suggestions_status_expires_at",
                table: "suggestions",
                columns: new[] { "status", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_state");

            migrationBuilder.DropTable(
                name: "skill_proposals");

            migrationBuilder.DropTable(
                name: "suggestion_detector_stats");

            migrationBuilder.DropTable(
                name: "suggestions");

            migrationBuilder.DropColumn(
                name: "input_embedding",
                table: "runs");
        }
    }
}
