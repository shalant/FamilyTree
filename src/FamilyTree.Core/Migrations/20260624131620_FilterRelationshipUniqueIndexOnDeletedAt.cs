using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTree.Core.Migrations
{
    /// <inheritdoc />
    public partial class FilterRelationshipUniqueIndexOnDeletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Relationships_PersonAId_PersonBId_Type",
                table: "Relationships");

            migrationBuilder.CreateIndex(
                name: "IX_Relationships_PersonAId_PersonBId_Type",
                table: "Relationships",
                columns: new[] { "PersonAId", "PersonBId", "Type" },
                unique: true,
                filter: "[DeletedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Relationships_PersonAId_PersonBId_Type",
                table: "Relationships");

            migrationBuilder.CreateIndex(
                name: "IX_Relationships_PersonAId_PersonBId_Type",
                table: "Relationships",
                columns: new[] { "PersonAId", "PersonBId", "Type" },
                unique: true);
        }
    }
}
