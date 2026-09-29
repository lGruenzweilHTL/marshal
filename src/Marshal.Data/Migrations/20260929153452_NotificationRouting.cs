using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class NotificationRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "deliver_after",
                table: "notifications",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "routing",
                table: "notifications",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<bool>(
                name: "deliveries_pending",
                table: "agent_tasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_deliver_after",
                table: "notifications",
                column: "deliver_after",
                filter: "deliver_after IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_agent_tasks_deliveries_pending",
                table: "agent_tasks",
                column: "deliveries_pending",
                filter: "deliveries_pending");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notifications_deliver_after",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_agent_tasks_deliveries_pending",
                table: "agent_tasks");

            migrationBuilder.DropColumn(
                name: "deliver_after",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "routing",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "deliveries_pending",
                table: "agent_tasks");
        }
    }
}
