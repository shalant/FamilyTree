using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTree.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddRelationshipImportBatchId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ImportBatchId",
                table: "Relationships",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Relationships_ImportBatchId",
                table: "Relationships",
                column: "ImportBatchId");

            migrationBuilder.AddForeignKey(
                name: "FK_Relationships_ImportBatches_ImportBatchId",
                table: "Relationships",
                column: "ImportBatchId",
                principalTable: "ImportBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Relationships_ImportBatches_ImportBatchId",
                table: "Relationships");

            migrationBuilder.DropIndex(
                name: "IX_Relationships_ImportBatchId",
                table: "Relationships");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "Relationships");
        }
    }
}
