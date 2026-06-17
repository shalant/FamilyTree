using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTree.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "Stories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Stories",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "Stories");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Stories");
        }
    }
}
