using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class NotesIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "note_index_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    path = table.Column<string>(type: "TEXT", nullable: false),
                    heading = table.Column<string>(type: "TEXT", nullable: false),
                    position = table.Column<int>(type: "INTEGER", nullable: false),
                    text = table.Column<string>(type: "TEXT", nullable: false),
                    file_hash = table.Column<string>(type: "TEXT", nullable: false),
                    indexed_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_note_index_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_note_index_entries_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_note_index_entries_owner_id_path",
                table: "note_index_entries",
                columns: new[] { "owner_id", "path" });

            // Keyword index and vector index bookkeeping (see NotesSchema); the sqlite-vec table itself is created at
            // runtime with the embedding model's dimension.
            migrationBuilder.Sql("CREATE VIRTUAL TABLE IF NOT EXISTS notes_fts USING fts5(entry_id UNINDEXED, owner_id UNINDEXED, path, heading, text, tokenize = 'unicode61 remove_diacritics 2')");
            migrationBuilder.Sql("CREATE TABLE IF NOT EXISTS notes_vector_index (id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1), model TEXT NOT NULL, dimensions INTEGER NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS note_vectors");
            migrationBuilder.Sql("DROP TABLE IF EXISTS notes_vector_index");
            migrationBuilder.Sql("DROP TABLE IF EXISTS notes_fts");

            migrationBuilder.DropTable(
                name: "note_index_entries");
        }
    }
}
