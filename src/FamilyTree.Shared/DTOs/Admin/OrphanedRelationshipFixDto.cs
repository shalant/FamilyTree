namespace FamilyTree.Shared.DTOs.Admin;

// One row per orphaned Relationship the integrity check found and auto-fixed —
// a live (non-deleted) Relationship whose PersonA or PersonB was soft-deleted
// outside PersonService.DeleteAsync's cascade. See DataIntegrityService.
public class OrphanedRelationshipFixDto
{
    public Guid RelationshipId { get; set; }
    public Guid PersonAId { get; set; }
    public string PersonAName { get; set; } = "";
    public Guid PersonBId { get; set; }
    public string PersonBName { get; set; } = "";
    public string Type { get; set; } = "";
}
