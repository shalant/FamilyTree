namespace FamilyTree.Shared.DTOs.Import;

public class ImportPersonDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? MiddleName { get; set; }
    public string? MaidenName { get; set; }
    public string[]? Aliases { get; set; }
    public string? BirthDate { get; set; }
    public string? DeathDate { get; set; }
    public bool IsDeceased { get; set; }
    public string Gender { get; set; } = "Unknown";
    public string? Notes { get; set; }
    public bool ReferenceOnly { get; set; }
    public bool Selected { get; set; } = true;
}

public class ImportRelationshipDto
{
    public string Type { get; set; } = "";  // "Parent" | "Spouse" | "Sibling"
    public int PersonAId { get; set; }
    public int PersonBId { get; set; }
    public bool IsFormer { get; set; }
    public string? MarriageDate { get; set; }
}

public class ImportPreview
{
    public List<ImportPersonDto> People { get; set; } = new();
    public List<ImportRelationshipDto> Relationships { get; set; } = new();
    public string? Warnings { get; set; }
}

public record ImportResult(int PeopleCreated, int RelationshipsCreated, string? Error = null);
