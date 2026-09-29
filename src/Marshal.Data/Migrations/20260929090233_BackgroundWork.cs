using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackgroundWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    parent_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_by = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    goal = table.Column<string>(type: "TEXT", nullable: false),
                    job = table.Column<string>(type: "TEXT", nullable: true),
                    template = table.Column<string>(type: "TEXT", nullable: true),
                    project_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    origin_satellite_id = table.Column<string>(type: "TEXT", nullable: true),
                    trigger_kind = table.Column<string>(type: "TEXT", nullable: false),
                    trigger_spec = table.Column<string>(type: "TEXT", nullable: false),
                    trigger_payload = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    status_reason = table.Column<string>(type: "TEXT", nullable: true),
                    next_run_at = table.Column<long>(type: "INTEGER", nullable: true),
                    notes = table.Column<string>(type: "TEXT", nullable: true),
                    budget = table.Column<string>(type: "TEXT", nullable: false),
                    spent = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: true),
                    deliveries = table.Column<string>(type: "TEXT", nullable: false),
                    override_quiet_hours_until = table.Column<long>(type: "INTEGER", nullable: true),
                    result_speech = table.Column<string>(type: "TEXT", nullable: true),
                    result_body = table.Column<string>(type: "TEXT", nullable: true),
                    result_at = table.Column<long>(type: "INTEGER", nullable: true),
                    depth = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_tasks_agent_tasks_parent_id",
                        column: x => x.parent_id,
                        principalTable: "agent_tasks",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_agent_tasks_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_agent_tasks_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "executor_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    task_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    executor = table.Column<string>(type: "TEXT", nullable: false),
                    handle = table.Column<string>(type: "TEXT", nullable: false),
                    callback_token_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    started_at = table.Column<long>(type: "INTEGER", nullable: false),
                    finished_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_executor_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_executor_sessions_agent_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "agent_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_executor_sessions_api_tokens_callback_token_id",
                        column: x => x.callback_token_id,
                        principalTable: "api_tokens",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_executor_sessions_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "follow_ups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    nonce = table.Column<string>(type: "TEXT", nullable: false),
                    satellite_id = table.Column<string>(type: "TEXT", nullable: false),
                    pending_action_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    task_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    linked_session_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    expires_at = table.Column<long>(type: "INTEGER", nullable: false),
                    consumed_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_follow_ups", x => x.id);
                    table.ForeignKey(
                        name: "fk_follow_ups_agent_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "agent_tasks",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_follow_ups_pending_actions_pending_action_id",
                        column: x => x.pending_action_id,
                        principalTable: "pending_actions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_follow_ups_sessions_linked_session_id",
                        column: x => x.linked_session_id,
                        principalTable: "sessions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_follow_ups_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    task_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    priority = table.Column<string>(type: "TEXT", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    channel = table.Column<string>(type: "TEXT", nullable: true),
                    outcome = table.Column<string>(type: "TEXT", nullable: false),
                    reason = table.Column<string>(type: "TEXT", nullable: true),
                    coalesced_into = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    delivered_at = table.Column<long>(type: "INTEGER", nullable: true),
                    consumed_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_agent_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "agent_tasks",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_notifications_notifications_coalesced_into",
                        column: x => x.coalesced_into,
                        principalTable: "notifications",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_notifications_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    task_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    target = table.Column<string>(type: "TEXT", nullable: false),
                    branch = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    diff = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    decided_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proposals", x => x.id);
                    table.ForeignKey(
                        name: "fk_proposals_agent_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "agent_tasks",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_proposals_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_runs_task_id",
                table: "runs",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_pending_actions_task_id",
                table: "pending_actions",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_tokens_task_id",
                table: "api_tokens",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_tasks_owner_id_name",
                table: "agent_tasks",
                columns: new[] { "owner_id", "name" },
                unique: true,
                filter: "name IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_agent_tasks_parent_id",
                table: "agent_tasks",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_tasks_project_id",
                table: "agent_tasks",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_tasks_status_next_run_at",
                table: "agent_tasks",
                columns: new[] { "status", "next_run_at" });

            migrationBuilder.CreateIndex(
                name: "ix_executor_sessions_callback_token_id",
                table: "executor_sessions",
                column: "callback_token_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_executor_sessions_owner_id",
                table: "executor_sessions",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_executor_sessions_task_id",
                table: "executor_sessions",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_linked_session_id",
                table: "follow_ups",
                column: "linked_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_nonce",
                table: "follow_ups",
                column: "nonce",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_owner_id",
                table: "follow_ups",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_pending_action_id",
                table: "follow_ups",
                column: "pending_action_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_satellite_id_expires_at",
                table: "follow_ups",
                columns: new[] { "satellite_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_task_id",
                table: "follow_ups",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_coalesced_into",
                table: "notifications",
                column: "coalesced_into");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_owner_id_created_at",
                table: "notifications",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_owner_id_outcome",
                table: "notifications",
                columns: new[] { "owner_id", "outcome" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_task_id",
                table: "notifications",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_proposals_owner_id_status",
                table: "proposals",
                columns: new[] { "owner_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_proposals_task_id",
                table: "proposals",
                column: "task_id");

            migrationBuilder.AddForeignKey(
                name: "fk_api_tokens_agent_tasks_task_id",
                table: "api_tokens",
                column: "task_id",
                principalTable: "agent_tasks",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_pending_actions_agent_tasks_task_id",
                table: "pending_actions",
                column: "task_id",
                principalTable: "agent_tasks",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_runs_agent_tasks_task_id",
                table: "runs",
                column: "task_id",
                principalTable: "agent_tasks",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_api_tokens_agent_tasks_task_id",
                table: "api_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_pending_actions_agent_tasks_task_id",
                table: "pending_actions");

            migrationBuilder.DropForeignKey(
                name: "fk_runs_agent_tasks_task_id",
                table: "runs");

            migrationBuilder.DropTable(
                name: "executor_sessions");

            migrationBuilder.DropTable(
                name: "follow_ups");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "proposals");

            migrationBuilder.DropTable(
                name: "agent_tasks");

            migrationBuilder.DropIndex(
                name: "ix_runs_task_id",
                table: "runs");

            migrationBuilder.DropIndex(
                name: "ix_pending_actions_task_id",
                table: "pending_actions");

            migrationBuilder.DropIndex(
                name: "ix_api_tokens_task_id",
                table: "api_tokens");
        }
    }
}
