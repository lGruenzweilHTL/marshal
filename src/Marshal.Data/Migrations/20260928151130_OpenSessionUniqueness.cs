using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class OpenSessionUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sessions_api_token_id_external_id",
                table: "sessions");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_api_token_id_external_id",
                table: "sessions",
                columns: new[] { "api_token_id", "external_id" },
                unique: true,
                filter: "closed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sessions_api_token_id_external_id",
                table: "sessions");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_api_token_id_external_id",
                table: "sessions",
                columns: new[] { "api_token_id", "external_id" },
                unique: true);
        }
    }
}
