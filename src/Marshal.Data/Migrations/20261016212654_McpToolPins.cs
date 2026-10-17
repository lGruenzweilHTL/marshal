using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class McpToolPins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mcp_tool_pins",
                columns: table => new
                {
                    server_id = table.Column<string>(type: "TEXT", nullable: false),
                    tool_name = table.Column<string>(type: "TEXT", nullable: false),
                    fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    state = table.Column<string>(type: "TEXT", nullable: false),
                    approved_fingerprint = table.Column<string>(type: "TEXT", nullable: true),
                    previous_definition_json = table.Column<string>(type: "TEXT", nullable: true),
                    current_definition_json = table.Column<string>(type: "TEXT", nullable: false),
                    first_seen = table.Column<long>(type: "INTEGER", nullable: false),
                    decided_at = table.Column<long>(type: "INTEGER", nullable: true),
                    decided_by = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mcp_tool_pins", x => new { x.server_id, x.tool_name });
                });

            migrationBuilder.CreateIndex(
                name: "ix_mcp_tool_pins_state",
                table: "mcp_tool_pins",
                column: "state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mcp_tool_pins");
        }
    }
}
