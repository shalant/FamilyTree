using FamilyTree.Shared.Enums;
using System.Text.Json.Serialization;

namespace FamilyTree.Shared.DTOs.Person;

public class PersonDto
{
    public Guid Id { get; set; }

    public string FirstName { get; set; } = "";
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = "";
    public string? MaidenName { get; set; }

    public DateOnly? BirthDate { get; set; }
    public string? BirthPlace { get; set; }

    public DateOnly? DeathDate { get; set; }
    public string? DeathPlace { get; set; }
    public int? Age => BirthDate.HasValue
        ? CalculateAge(BirthDate.Value, DeathDate)
        : null;
    public string? BiographyNotes { get; set; }
    public string? ProfilePhotoUrl { get; set; }

    public Gender? Gender { get; set; }

    public string FullName => string.IsNullOrWhiteSpace(MiddleName)
        ? $"{FirstName} {LastName}".Trim()
        : $"{FirstName} {MiddleName} {LastName}".Trim();

    // Age stays as computed — remove the setter, let PersonMapper set nothing
    // OR keep as settable and have PersonMapper set it. 
    // Since PersonDto is a DTO (crosses HTTP or process boundary), 
    // settable is more correct — computed properties can't be deserialized.
    // Keep { get; set; } but also keep PersonMapper setting it.    public bool IsDeceased => DeathDate.HasValue;
    //public int? Age { get; set; }

    // Needed by the tree
    public List<Guid>? ParentIds { get; set; }
    public List<Guid>? ChildIds { get; set; }
    public List<Guid>? SpouseIds { get; set; }
    public List<Guid>? FormerSpouseIds { get; set; }
    public List<Guid>? SiblingIds { get; set; }

    // Set at render time by FamilyTreeCanvas — not persisted
    [JsonIgnore]
    public int GenerationDepth { get; set; }
    public byte[]? RowVersion { get; set; }

    private static int CalculateAge(DateOnly birth, DateOnly? death)
    {
        var end = death ?? DateOnly.FromDateTime(DateTime.Today);
        var age = end.Year - birth.Year;
        if (end < birth.AddYears(age)) age--;
        return age;
    }

    public bool IsDeceased => DeathDate.HasValue;

    // Age stays computed — do NOT add a setter.
    // Computed properties are fine here because this DTO is only used for reads.

}