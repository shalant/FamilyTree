using FamilyTree.Shared.Enums;

namespace FamilyTree.Shared.DTOs.Relationship;

public class RelationshipUpsertDto
{
    public Guid PersonAId { get; set; }
    public Guid PersonBId { get; set; }
    public RelationshipType Type { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Notes { get; set; }
}