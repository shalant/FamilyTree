using FamilyTree.Shared.Enums;

namespace FamilyTree.Shared.DTOs.Person;

public class PersonUpsertDto
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateOnly? BirthDate { get; set; }
    public DateOnly? DeathDate { get; set; }
    public string? BiographyNotes { get; set; }
    public List<Guid> ParentIds { get; set; } = new();
    public List<Guid> SpouseIds { get; set; } = new();
    public List<Guid> ChildIds { get; set; } = new();
    public List<Guid> SiblingIds { get; set; } = new();
    // Also missing the extra fields Person has:
    public string? MiddleName { get; set; }
    public string? MaidenName { get; set; }
    public string? BirthPlace { get; set; }
    public string? DeathPlace { get; set; }
    public string? ProfilePhotoUrl { get; set; }
    public Gender? Gender { get; set; }
}