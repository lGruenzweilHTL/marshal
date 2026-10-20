using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class MemoryTimeAndIdle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "summary",
                table: "projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "summary_updated_at",
                table: "projects",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "valid_from",
                table: "chunks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "valid_to",
                table: "chunks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "valid_from",
                table: "chunk_versions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "valid_to",
                table: "chunk_versions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "next_prepare_at",
                table: "agent_tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "prepare_before",
                table: "agent_tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "prepare_goal",
                table: "agent_tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "prepared_at",
                table: "agent_tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "prepared_notes",
                table: "agent_tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "prepared_taint",
                table: "agent_tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "memory_proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    chunk_a = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    chunk_b = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    suggestion_json = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    decided_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memory_proposals", x => x.id);
                    table.ForeignKey(
                        name: "fk_memory_proposals_chunks_chunk_a",
                        column: x => x.chunk_a,
                        principalTable: "chunks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_memory_proposals_chunks_chunk_b",
                        column: x => x.chunk_b,
                        principalTable: "chunks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_memory_proposals_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_tasks_next_prepare_at",
                table: "agent_tasks",
                column: "next_prepare_at",
                filter: "next_prepare_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_memory_proposals_chunk_a",
                table: "memory_proposals",
                column: "chunk_a");

            migrationBuilder.CreateIndex(
                name: "ix_memory_proposals_chunk_b",
                table: "memory_proposals",
                column: "chunk_b");

            migrationBuilder.CreateIndex(
                name: "ix_memory_proposals_owner_id_kind_chunk_a_chunk_b",
                table: "memory_proposals",
                columns: new[] { "owner_id", "kind", "chunk_a", "chunk_b" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_memory_proposals_owner_id_status",
                table: "memory_proposals",
                columns: new[] { "owner_id", "status" });

            // Facts were true since they were stored; a version was true from its chunk's creation until it was replaced.
            migrationBuilder.Sql("UPDATE chunks SET valid_from = created_at");
            migrationBuilder.Sql("UPDATE chunk_versions SET valid_from = (SELECT created_at FROM chunks WHERE chunks.id = chunk_versions.chunk_id), valid_to = replaced_at");

            // Rows are added by the memory index startup (owner ids in the index tables use the store's format).
            migrationBuilder.Sql("CREATE VIRTUAL TABLE IF NOT EXISTS chunk_versions_fts USING fts5(version_id UNINDEXED, chunk_id UNINDEXED, owner_id UNINDEXED, content, tokenize = 'unicode61 remove_diacritics 2')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS chunk_versions_fts");

            migrationBuilder.DropTable(
                name: "memory_proposals");

            migrationBuilder.DropIndex(
                name: "ix_agent_tasks_next_prepare_at",
                table: "agent_tasks");

            migrationBuilder.DropColumn(
                name: "summary",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "summary_updated_at",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "valid_from",
                table: "chunks");

            migrationBuilder.DropColumn(
                name: "valid_to",
                table: "chunks");

            migrationBuilder.DropColumn(
                name: "valid_from",
                table: "chunk_versions");

            migrationBuilder.DropColumn(
                name: "valid_to",
                table: "chunk_versions");

            migrationBuilder.DropColumn(
                name: "next_prepare_at",
                table: "agent_tasks");

            migrationBuilder.DropColumn(
                name: "prepare_before",
                table: "agent_tasks");

            migrationBuilder.DropColumn(
                name: "prepare_goal",
                table: "agent_tasks");

            migrationBuilder.DropColumn(
                name: "prepared_at",
                table: "agent_tasks");

            migrationBuilder.DropColumn(
                name: "prepared_notes",
                table: "agent_tasks");

            migrationBuilder.DropColumn(
                name: "prepared_taint",
                table: "agent_tasks");
        }
    }
}
