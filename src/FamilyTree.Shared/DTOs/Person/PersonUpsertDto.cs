using System.ComponentModel.DataAnnotations;
using FamilyTree.Shared.Enums;

namespace FamilyTree.Shared.DTOs.Person;

public class PersonUpsertDto
{
    [Required(ErrorMessage = "First name is required.")]
    [StringLength(100, ErrorMessage = "First name cannot exceed 100 characters.")]
    public string FirstName { get; set; } = "";

    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(100, ErrorMessage = "Last name cannot exceed 100 characters.")]
    public string LastName { get; set; } = "";

    [StringLength(100, ErrorMessage = "Middle name cannot exceed 100 characters.")]
    public string? MiddleName { get; set; }

    [StringLength(100, ErrorMessage = "Maiden name cannot exceed 100 characters.")]
    public string? MaidenName { get; set; }

    public DateOnly? BirthDate { get; set; }

    [StringLength(200, ErrorMessage = "Birth place cannot exceed 200 characters.")]
    public string? BirthPlace { get; set; }

    public DateOnly? DeathDate { get; set; }

    [StringLength(200, ErrorMessage = "Death place cannot exceed 200 characters.")]
    public string? DeathPlace { get; set; }

    [StringLength(5000, ErrorMessage = "Biography cannot exceed 5000 characters.")]
    public string? BiographyNotes { get; set; }

    [StringLength(500, ErrorMessage = "Profile photo URL cannot exceed 500 characters.")]
    public string? ProfilePhotoUrl { get; set; }

    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256, ErrorMessage = "Email cannot exceed 256 characters.")]
    public string? Email { get; set; }

    public Gender? Gender { get; set; }

    public List<Guid> ParentIds { get; set; } = new();
    public List<Guid> SpouseIds { get; set; } = new();
    public List<Guid> FormerSpouseIds { get; set; } = new();
    public List<Guid> ChildIds { get; set; } = new();
    public List<Guid> SiblingIds { get; set; } = new();
}