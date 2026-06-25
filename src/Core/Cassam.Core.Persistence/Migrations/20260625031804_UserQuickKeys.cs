using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cassam.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserQuickKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "quick_keys",
                table: "users",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "quick_keys",
                table: "users");
        }
    }
}
