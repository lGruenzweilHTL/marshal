using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class ToolRetrieval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "loaded_tool_namespaces",
                table: "sessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "time_to_first_delta_ms",
                table: "runs",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "loaded_tool_namespaces",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "time_to_first_delta_ms",
                table: "runs");
        }
    }
}
