using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_name = table.Column<string>(type: "TEXT", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", nullable: false),
                    password_hash = table.Column<string>(type: "TEXT", nullable: true),
                    security_stamp = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "api_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    token_hash = table.Column<string>(type: "TEXT", nullable: false),
                    scopes = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    task_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    expires_at = table.Column<long>(type: "INTEGER", nullable: true),
                    revoked_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_api_tokens_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "secrets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    ciphertext = table.Column<byte[]>(type: "BLOB", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_secrets", x => x.id);
                    table.ForeignKey(
                        name: "fk_secrets_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "user_preferences",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    language = table.Column<string>(type: "TEXT", nullable: false),
                    time_zone = table.Column<string>(type: "TEXT", nullable: false),
                    quiet_hours = table.Column<string>(type: "TEXT", nullable: true),
                    notification_targets = table.Column<string>(type: "TEXT", nullable: true),
                    dnd_until = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_preferences", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_preferences_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    api_token_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    external_id = table.Column<string>(type: "TEXT", nullable: true),
                    identity_evidence = table.Column<string>(type: "TEXT", nullable: false),
                    resolved_by = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    last_turn_at = table.Column<long>(type: "INTEGER", nullable: false),
                    closed_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_sessions_api_tokens_api_token_id",
                        column: x => x.api_token_id,
                        principalTable: "api_tokens",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sessions_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pending_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    session_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    task_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    satellite_id = table.Column<string>(type: "TEXT", nullable: true),
                    tool = table.Column<string>(type: "TEXT", nullable: false),
                    args = table.Column<string>(type: "TEXT", nullable: false),
                    summary = table.Column<string>(type: "TEXT", nullable: false),
                    reasons = table.Column<string>(type: "TEXT", nullable: false),
                    nonce = table.Column<string>(type: "TEXT", nullable: false),
                    language = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    expires_at = table.Column<long>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    decided_by = table.Column<string>(type: "TEXT", nullable: true),
                    decided_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_actions", x => x.id);
                    table.ForeignKey(
                        name: "fk_pending_actions_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_pending_actions_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    session_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    task_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    source = table.Column<string>(type: "TEXT", nullable: false),
                    input_mode = table.Column<string>(type: "TEXT", nullable: true),
                    satellite_id = table.Column<string>(type: "TEXT", nullable: true),
                    area_id = table.Column<string>(type: "TEXT", nullable: true),
                    language = table.Column<string>(type: "TEXT", nullable: true),
                    trigger = table.Column<string>(type: "TEXT", nullable: false),
                    started_at = table.Column<long>(type: "INTEGER", nullable: false),
                    ended_at = table.Column<long>(type: "INTEGER", nullable: true),
                    outcome = table.Column<string>(type: "TEXT", nullable: false),
                    outcome_reason = table.Column<string>(type: "TEXT", nullable: true),
                    tainted = table.Column<bool>(type: "INTEGER", nullable: false),
                    taint_sources = table.Column<string>(type: "TEXT", nullable: true),
                    cost = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_runs_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_runs_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    actor_kind = table.Column<string>(type: "TEXT", nullable: false),
                    actor_id = table.Column<string>(type: "TEXT", nullable: true),
                    run_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    action = table.Column<string>(type: "TEXT", nullable: false),
                    target = table.Column<string>(type: "TEXT", nullable: true),
                    details = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_events_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_audit_events_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "run_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    run_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    model = table.Column<string>(type: "TEXT", nullable: true),
                    tier = table.Column<string>(type: "TEXT", nullable: true),
                    tokens_in = table.Column<int>(type: "INTEGER", nullable: true),
                    tokens_out = table.Column<int>(type: "INTEGER", nullable: true),
                    tokens_cached = table.Column<int>(type: "INTEGER", nullable: true),
                    first_token_ms = table.Column<int>(type: "INTEGER", nullable: true),
                    tool = table.Column<string>(type: "TEXT", nullable: true),
                    risk_tier = table.Column<string>(type: "TEXT", nullable: true),
                    guard_decision = table.Column<string>(type: "TEXT", nullable: true),
                    payload = table.Column<string>(type: "TEXT", nullable: true),
                    duration_ms = table.Column<int>(type: "INTEGER", nullable: false),
                    cost = table.Column<double>(type: "REAL", nullable: true),
                    error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_run_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_run_steps_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_tokens_owner_id",
                table: "api_tokens",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_tokens_token_hash",
                table: "api_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_owner_id_created_at",
                table: "audit_events",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_run_id",
                table: "audit_events",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_pending_actions_owner_id",
                table: "pending_actions",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_pending_actions_session_id_status",
                table: "pending_actions",
                columns: new[] { "session_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_run_steps_run_id_sequence",
                table: "run_steps",
                columns: new[] { "run_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_runs_owner_id_started_at",
                table: "runs",
                columns: new[] { "owner_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_runs_session_id",
                table: "runs",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_secrets_instance_name",
                table: "secrets",
                column: "name",
                unique: true,
                filter: "owner_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_secrets_owner_id_name",
                table: "secrets",
                columns: new[] { "owner_id", "name" },
                unique: true,
                filter: "owner_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_api_token_id_external_id",
                table: "sessions",
                columns: new[] { "api_token_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_owner_id",
                table: "sessions",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_user_name",
                table: "users",
                column: "user_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "pending_actions");

            migrationBuilder.DropTable(
                name: "run_steps");

            migrationBuilder.DropTable(
                name: "secrets");

            migrationBuilder.DropTable(
                name: "user_preferences");

            migrationBuilder.DropTable(
                name: "runs");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropTable(
                name: "api_tokens");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
