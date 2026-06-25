using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTree.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddImportBatchReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Report",
                table: "ImportBatches",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Report",
                table: "ImportBatches");
        }
    }
}
