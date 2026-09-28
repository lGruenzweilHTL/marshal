using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marshal.Data.Migrations
{
    /// <inheritdoc />
    public partial class RunStepDepth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "depth",
                table: "run_steps",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "depth",
                table: "run_steps");
        }
    }
}
