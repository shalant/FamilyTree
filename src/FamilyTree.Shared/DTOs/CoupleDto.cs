namespace FamilyTree.Shared.DTOs;

/// <summary>
/// Passed to FamilyTreeCanvas to define couple relationships
/// and their shared children. Derived from RelationshipDto
/// data in the parent page — the canvas itself has no DB knowledge.
/// </summary>
public class CoupleDto
{
    public Guid PersonAId { get; set; }
    public Guid PersonBId { get; set; }
    public List<Guid> ChildIds { get; set; } = [];
}