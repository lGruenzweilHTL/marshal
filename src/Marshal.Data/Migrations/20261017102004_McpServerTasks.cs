using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class McpServerTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "server_task_id",
                table: "pending_input_requests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "server_task_input_key",
                table: "pending_input_requests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "mcp_task_handles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    server_id = table.Column<string>(type: "TEXT", nullable: false),
                    server_task_id = table.Column<string>(type: "TEXT", nullable: false),
                    tool = table.Column<string>(type: "TEXT", nullable: false),
                    task_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    language = table.Column<string>(type: "TEXT", nullable: false),
                    taint_kind = table.Column<string>(type: "TEXT", nullable: true),
                    poll_interval_ms = table.Column<long>(type: "INTEGER", nullable: true),
                    asked_inputs = table.Column<string>(type: "TEXT", nullable: false),
                    polls = table.Column<int>(type: "INTEGER", nullable: false),
                    next_poll_at = table.Column<long>(type: "INTEGER", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mcp_task_handles", x => x.id);
                    table.ForeignKey(
                        name: "fk_mcp_task_handles_agent_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "agent_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mcp_task_handles_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mcp_task_handles_next_poll_at",
                table: "mcp_task_handles",
                column: "next_poll_at");

            migrationBuilder.CreateIndex(
                name: "ix_mcp_task_handles_owner_id",
                table: "mcp_task_handles",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_mcp_task_handles_server_id_server_task_id",
                table: "mcp_task_handles",
                columns: new[] { "server_id", "server_task_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mcp_task_handles_task_id",
                table: "mcp_task_handles",
                column: "task_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mcp_task_handles");

            migrationBuilder.DropColumn(
                name: "server_task_id",
                table: "pending_input_requests");

            migrationBuilder.DropColumn(
                name: "server_task_input_key",
                table: "pending_input_requests");
        }
    }
}
