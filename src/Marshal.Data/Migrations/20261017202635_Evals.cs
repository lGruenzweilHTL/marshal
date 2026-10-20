using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class Evals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "help_marshal_learn",
                table: "user_preferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "rated_at",
                table: "runs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rating",
                table: "runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rating_note",
                table: "runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "eval_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_run_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    expectation = table.Column<string>(type: "TEXT", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    input = table.Column<string>(type: "TEXT", nullable: false),
                    input_mode = table.Column<string>(type: "TEXT", nullable: false),
                    language = table.Column<string>(type: "TEXT", nullable: false),
                    satellite_id = table.Column<string>(type: "TEXT", nullable: true),
                    area_id = table.Column<string>(type: "TEXT", nullable: true),
                    history = table.Column<string>(type: "TEXT", nullable: false),
                    turn_context = table.Column<string>(type: "TEXT", nullable: true),
                    tool_calls = table.Column<string>(type: "TEXT", nullable: false),
                    reply = table.Column<string>(type: "TEXT", nullable: true),
                    incomplete = table.Column<bool>(type: "INTEGER", nullable: false),
                    recorded_at = table.Column<long>(type: "INTEGER", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eval_cases", x => x.id);
                    table.ForeignKey(
                        name: "fk_eval_cases_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "eval_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    candidate = table.Column<string>(type: "TEXT", nullable: false),
                    case_ids = table.Column<string>(type: "TEXT", nullable: false),
                    started_by = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    pass = table.Column<int>(type: "INTEGER", nullable: false),
                    changed = table.Column<int>(type: "INTEGER", nullable: false),
                    diverged = table.Column<int>(type: "INTEGER", nullable: false),
                    failed = table.Column<int>(type: "INTEGER", nullable: false),
                    cost = table.Column<double>(type: "REAL", nullable: false),
                    estimated_cost = table.Column<double>(type: "REAL", nullable: false),
                    error = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    ended_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eval_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_eval_runs_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "eval_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    run_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    case_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    outcome = table.Column<string>(type: "TEXT", nullable: false),
                    calls = table.Column<string>(type: "TEXT", nullable: false),
                    reply = table.Column<string>(type: "TEXT", nullable: true),
                    judge_score = table.Column<double>(type: "REAL", nullable: true),
                    cost = table.Column<double>(type: "REAL", nullable: false),
                    error = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eval_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_eval_results_eval_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "eval_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_eval_results_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_eval_cases_owner_id_source_run_id",
                table: "eval_cases",
                columns: new[] { "owner_id", "source_run_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_eval_results_owner_id_created_at",
                table: "eval_results",
                columns: new[] { "owner_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_eval_results_run_id_created_at",
                table: "eval_results",
                columns: new[] { "run_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_eval_runs_owner_id_created_at",
                table: "eval_runs",
                columns: new[] { "owner_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "eval_cases");

            migrationBuilder.DropTable(
                name: "eval_results");

            migrationBuilder.DropTable(
                name: "eval_runs");

            migrationBuilder.DropColumn(
                name: "help_marshal_learn",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "rated_at",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "rating",
                table: "runs");

            migrationBuilder.DropColumn(
                name: "rating_note",
                table: "runs");
        }
    }
}
