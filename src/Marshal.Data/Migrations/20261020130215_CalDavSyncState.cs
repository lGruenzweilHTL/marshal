using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class CalDavSyncState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "caldav_sync_state",
                columns: table => new
                {
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    account = table.Column<string>(type: "TEXT", nullable: false),
                    calendar = table.Column<string>(type: "TEXT", nullable: false),
                    sync_token = table.Column<string>(type: "TEXT", nullable: true),
                    etag_map_json = table.Column<string>(type: "TEXT", nullable: false),
                    last_poll = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_caldav_sync_state", x => new { x.owner_id, x.account, x.calendar });
                    table.ForeignKey(
                        name: "fk_caldav_sync_state_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "caldav_sync_state");
        }
    }
}
