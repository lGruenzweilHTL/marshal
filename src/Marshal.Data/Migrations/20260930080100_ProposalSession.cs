using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProposalSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "executor_session_id",
                table: "proposals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_proposals_executor_session_id",
                table: "proposals",
                column: "executor_session_id");

            migrationBuilder.AddForeignKey(
                name: "fk_proposals_executor_sessions_executor_session_id",
                table: "proposals",
                column: "executor_session_id",
                principalTable: "executor_sessions",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_proposals_executor_sessions_executor_session_id",
                table: "proposals");

            migrationBuilder.DropIndex(
                name: "ix_proposals_executor_session_id",
                table: "proposals");

            migrationBuilder.DropColumn(
                name: "executor_session_id",
                table: "proposals");
        }
    }
}
