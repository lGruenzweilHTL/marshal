using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConversationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "rested_at",
                table: "sessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "summarized_until",
                table: "sessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "origin_session_id",
                table: "agent_tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_tasks_origin_session_id",
                table: "agent_tasks",
                column: "origin_session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_agent_tasks_origin_session_id",
                table: "agent_tasks");

            migrationBuilder.DropColumn(
                name: "rested_at",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "summarized_until",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "origin_session_id",
                table: "agent_tasks");
        }
    }
}
