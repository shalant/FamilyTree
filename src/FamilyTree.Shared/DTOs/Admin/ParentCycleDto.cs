namespace FamilyTree.Shared.DTOs.Admin;

// One group of people caught in a Parent loop (each is their own ancestor), found by
// DataIntegrityService.FindParentCyclesAsync. Read-only report: nothing is changed.
// Removing any ONE of the listed links breaks the loop.
public class ParentCycleDto
{
    public List<ParentCyclePersonDto> People { get; set; } = [];
    public List<ParentCycleLinkDto> Links { get; set; } = [];
}

public class ParentCyclePersonDto
{
    public Guid PersonId { get; set; }
    public string Name { get; set; } = "";
    public Guid? FamilyId { get; set; }
}

// A "ParentName is a parent of ChildName" row that sits inside the loop.
public class ParentCycleLinkDto
{
    public Guid RelationshipId { get; set; }
    public Guid ParentId { get; set; }
    public string ParentName { get; set; } = "";
    public Guid ChildId { get; set; }
    public string ChildName { get; set; } = "";
}
