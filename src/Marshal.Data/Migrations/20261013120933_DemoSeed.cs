using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class DemoSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "demo_seed",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    owner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    entity = table.Column<string>(type: "TEXT", nullable: false),
                    row_id = table.Column<string>(type: "TEXT", nullable: false),
                    seeded_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_demo_seed", x => x.id);
                    table.ForeignKey(
                        name: "fk_demo_seed_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_demo_seed_owner_id_entity",
                table: "demo_seed",
                columns: new[] { "owner_id", "entity" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "demo_seed");
        }
    }
}
