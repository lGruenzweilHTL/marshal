using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class Memory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    centroid = table.Column<byte[]>(type: "BLOB", nullable: true),
                    last_active_at = table.Column<long>(type: "INTEGER", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    deleted_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                    table.ForeignKey(
                        name: "fk_projects_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chunks",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    scope = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    type = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    importance = table.Column<int>(type: "INTEGER", nullable: false),
                    pinned = table.Column<bool>(type: "INTEGER", nullable: false),
                    source_ref = table.Column<string>(type: "TEXT", nullable: true),
                    created_by = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false),
                    deleted_at = table.Column<long>(type: "INTEGER", nullable: true),
                    last_retrieved_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chunks", x => x.id);
                    table.CheckConstraint("ck_chunks_scope_project", "(scope = 'project') = (project_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_chunks_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_chunks_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_aliases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    project_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    phrase = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    confirmations = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_aliases", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_aliases_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_feedback",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    project_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    run_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    phrase = table.Column<string>(type: "TEXT", nullable: false),
                    embedding = table.Column<byte[]>(type: "BLOB", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_feedback", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_feedback_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_project_feedback_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "chunk_edges",
                columns: table => new
                {
                    from_id = table.Column<string>(type: "TEXT", nullable: false),
                    to_id = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chunk_edges", x => new { x.from_id, x.to_id, x.kind });
                    table.CheckConstraint("ck_chunk_edges_no_self_supersede", "kind <> 'supersedes' OR from_id <> to_id");
                    table.ForeignKey(
                        name: "fk_chunk_edges_chunks_from_id",
                        column: x => x.from_id,
                        principalTable: "chunks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_chunk_edges_chunks_to_id",
                        column: x => x.to_id,
                        principalTable: "chunks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chunk_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    chunk_id = table.Column<string>(type: "TEXT", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    importance = table.Column<int>(type: "INTEGER", nullable: false),
                    source_ref = table.Column<string>(type: "TEXT", nullable: true),
                    replaced_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chunk_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_chunk_versions_chunks_chunk_id",
                        column: x => x.chunk_id,
                        principalTable: "chunks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chunk_edges_to_id",
                table: "chunk_edges",
                column: "to_id");

            migrationBuilder.CreateIndex(
                name: "ix_chunk_versions_chunk_id_replaced_at",
                table: "chunk_versions",
                columns: new[] { "chunk_id", "replaced_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chunks_owner_id_pinned",
                table: "chunks",
                columns: new[] { "owner_id", "pinned" });

            migrationBuilder.CreateIndex(
                name: "ix_chunks_project_id",
                table: "chunks",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_aliases_project_id_phrase",
                table: "project_aliases",
                columns: new[] { "project_id", "phrase" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_feedback_project_id_created_at",
                table: "project_feedback",
                columns: new[] { "project_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_project_feedback_run_id",
                table: "project_feedback",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_owner_id_name",
                table: "projects",
                columns: new[] { "owner_id", "name" });

            // Keyword index and vector index bookkeeping (see MemorySchema); the sqlite-vec table itself is created at
            // runtime with the embedding model's dimension.
            migrationBuilder.Sql("CREATE VIRTUAL TABLE IF NOT EXISTS chunks_fts USING fts5(chunk_id UNINDEXED, owner_id UNINDEXED, title, content, tokenize = 'unicode61 remove_diacritics 2')");
            migrationBuilder.Sql("CREATE TABLE IF NOT EXISTS memory_vector_index (id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1), model TEXT NOT NULL, dimensions INTEGER NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS chunk_vectors");
            migrationBuilder.Sql("DROP TABLE IF EXISTS memory_vector_index");
            migrationBuilder.Sql("DROP TABLE IF EXISTS chunks_fts");

            migrationBuilder.DropTable(
                name: "chunk_edges");

            migrationBuilder.DropTable(
                name: "chunk_versions");

            migrationBuilder.DropTable(
                name: "project_aliases");

            migrationBuilder.DropTable(
                name: "project_feedback");

            migrationBuilder.DropTable(
                name: "chunks");

            migrationBuilder.DropTable(
                name: "projects");
        }
    }
}
