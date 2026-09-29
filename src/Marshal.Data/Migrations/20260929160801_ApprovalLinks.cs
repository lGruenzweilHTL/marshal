using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "run_id",
                table: "pending_actions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "taint",
                table: "pending_actions",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateIndex(
                name: "ix_pending_actions_nonce",
                table: "pending_actions",
                column: "nonce",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pending_actions_status_expires_at",
                table: "pending_actions",
                columns: new[] { "status", "expires_at" },
                filter: "task_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pending_actions_nonce",
                table: "pending_actions");

            migrationBuilder.DropIndex(
                name: "ix_pending_actions_status_expires_at",
                table: "pending_actions");

            migrationBuilder.DropColumn(
                name: "run_id",
                table: "pending_actions");

            migrationBuilder.DropColumn(
                name: "taint",
                table: "pending_actions");
        }
    }
}
