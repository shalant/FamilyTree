using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTree.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthorName",
                table: "Stories",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InviteId",
                table: "Stories",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StoryInvites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "newsequentialid()"),
                    Token = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UnlinkedPersonName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    InvitedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonalNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsUsed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoryInvites_AspNetUsers_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoryInvites_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stories_InviteId",
                table: "Stories",
                column: "InviteId");

            migrationBuilder.CreateIndex(
                name: "IX_StoryInvites_InvitedByUserId",
                table: "StoryInvites",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoryInvites_PersonId",
                table: "StoryInvites",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_StoryInvites_Token",
                table: "StoryInvites",
                column: "Token",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Stories_StoryInvites_InviteId",
                table: "Stories",
                column: "InviteId",
                principalTable: "StoryInvites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Stories_StoryInvites_InviteId",
                table: "Stories");

            migrationBuilder.DropTable(
                name: "StoryInvites");

            migrationBuilder.DropIndex(
                name: "IX_Stories_InviteId",
                table: "Stories");

            migrationBuilder.DropColumn(
                name: "AuthorName",
                table: "Stories");

            migrationBuilder.DropColumn(
                name: "InviteId",
                table: "Stories");
        }
    }
}
