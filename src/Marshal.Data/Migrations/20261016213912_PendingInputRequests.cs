using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class PendingInputRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pending_input_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    session_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    satellite_id = table.Column<string>(type: "TEXT", nullable: true),
                    task_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    run_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    server_id = table.Column<string>(type: "TEXT", nullable: false),
                    tool = table.Column<string>(type: "TEXT", nullable: false),
                    args = table.Column<string>(type: "TEXT", nullable: false),
                    previous_answers = table.Column<string>(type: "TEXT", nullable: false),
                    mode = table.Column<string>(type: "TEXT", nullable: false),
                    message = table.Column<string>(type: "TEXT", nullable: false),
                    requested_schema = table.Column<string>(type: "TEXT", nullable: true),
                    url = table.Column<string>(type: "TEXT", nullable: true),
                    nonce = table.Column<string>(type: "TEXT", nullable: false),
                    language = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    expires_at = table.Column<long>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    decided_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_input_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_pending_input_requests_agent_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "agent_tasks",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_pending_input_requests_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_pending_input_requests_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pending_input_requests_nonce",
                table: "pending_input_requests",
                column: "nonce",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pending_input_requests_owner_id",
                table: "pending_input_requests",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_pending_input_requests_session_id",
                table: "pending_input_requests",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_pending_input_requests_status_expires_at",
                table: "pending_input_requests",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pending_input_requests_task_id",
                table: "pending_input_requests",
                column: "task_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pending_input_requests");
        }
    }
}
