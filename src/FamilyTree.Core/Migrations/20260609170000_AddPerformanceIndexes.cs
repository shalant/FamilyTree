using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTree.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE INDEX IX_People_Family_Active
                    ON People (FamilyId, LastName, FirstName) WHERE DeletedAt IS NULL;
                CREATE INDEX IX_Relationships_A_Active
                    ON Relationships (PersonAId) WHERE DeletedAt IS NULL;
                CREATE INDEX IX_Relationships_B_Active
                    ON Relationships (PersonBId) WHERE DeletedAt IS NULL;
                CREATE INDEX IX_AuditLog_Entity
                    ON AuditLogs (EntityType, EntityId, Timestamp DESC);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS IX_People_Family_Active ON People;
                DROP INDEX IF EXISTS IX_Relationships_A_Active ON Relationships;
                DROP INDEX IF EXISTS IX_Relationships_B_Active ON Relationships;
                DROP INDEX IF EXISTS IX_AuditLog_Entity ON AuditLogs;
            ");
        }
    }
}
