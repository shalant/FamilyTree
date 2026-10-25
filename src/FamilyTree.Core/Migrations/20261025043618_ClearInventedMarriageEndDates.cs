using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTree.Core.Migrations
{
    /// <inheritdoc />
    public partial class ClearInventedMarriageEndDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEnded",
                table: "Relationships",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Until now EndDate != null was the only thing marking a marriage as ended.
            migrationBuilder.Sql(
                "UPDATE Relationships SET IsEnded = 1 WHERE Type = 'Spouse' AND EndDate IS NOT NULL;");

            // No screen has ever let anyone type an end date: every EndDate on file was a
            // default, either the earlier partner's death date or the day the marriage was
            // marked as ended (Lillian and Louis: died 1922, recorded as ended 2026-09-18).
            // Recompute each one under today's rule: the earlier death date, else unknown.
            migrationBuilder.Sql("""
                UPDATE r
                SET EndDate = CASE
                        WHEN a.DeathDate IS NULL THEN b.DeathDate
                        WHEN b.DeathDate IS NULL THEN a.DeathDate
                        WHEN a.DeathDate < b.DeathDate THEN a.DeathDate
                        ELSE b.DeathDate
                    END
                FROM Relationships r
                JOIN People a ON a.Id = r.PersonAId
                JOIN People b ON b.Id = r.PersonBId
                WHERE r.Type = 'Spouse'
                  AND r.EndDate IS NOT NULL
                  AND (a.DeathDate IS NULL OR r.EndDate <> a.DeathDate)
                  AND (b.DeathDate IS NULL OR r.EndDate <> b.DeathDate);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The invented dates are not restored. An ended marriage with no date would
            // read as active without IsEnded, so give it back a placeholder date first.
            migrationBuilder.Sql(
                "UPDATE Relationships SET EndDate = CAST(COALESCE(UpdatedAt, CreatedAt, GETUTCDATE()) AS date) " +
                "WHERE IsEnded = 1 AND EndDate IS NULL;");

            migrationBuilder.DropColumn(
                name: "IsEnded",
                table: "Relationships");
        }
    }
}
