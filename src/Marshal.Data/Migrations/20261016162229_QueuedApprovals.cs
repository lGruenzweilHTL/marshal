using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class QueuedApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pending_actions_status_expires_at",
                table: "pending_actions");

            migrationBuilder.AddColumn<bool>(
                name: "queued",
                table: "pending_actions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Every approval so far belonged to a task and sat in the approval queue.
            migrationBuilder.Sql("UPDATE pending_actions SET queued = 1 WHERE task_id IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "ix_pending_actions_status_expires_at",
                table: "pending_actions",
                columns: new[] { "status", "expires_at" },
                filter: "queued = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pending_actions_status_expires_at",
                table: "pending_actions");

            migrationBuilder.DropColumn(
                name: "queued",
                table: "pending_actions");

            migrationBuilder.CreateIndex(
                name: "ix_pending_actions_status_expires_at",
                table: "pending_actions",
                columns: new[] { "status", "expires_at" },
                filter: "task_id IS NOT NULL");
        }
    }
}
