using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTree.Core.Migrations
{
    /// <summary>
    /// S7 (docs/security.md, 2026-10-25): AppUser.IsAdmin becomes the only source of the
    /// Admin role. The Users tab used to write an Identity "Admin" role instead, which
    /// nothing created or read back. Anyone holding that role keeps Admin through IsAdmin,
    /// then the role membership is removed so there aren't two sources to disagree.
    /// </summary>
    public partial class FoldAdminRoleIntoIsAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE u SET u.IsAdmin = 1
                FROM AspNetUsers u
                JOIN AspNetUserRoles ur ON ur.UserId = u.Id
                JOIN AspNetRoles r ON r.Id = ur.RoleId
                WHERE r.NormalizedName = 'ADMIN';

                DELETE ur
                FROM AspNetUserRoles ur
                JOIN AspNetRoles r ON r.Id = ur.RoleId
                WHERE r.NormalizedName = 'ADMIN';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: IsAdmin keeps every Admin, and the old role rows granted
            // nothing IsAdmin doesn't.
        }
    }
}
