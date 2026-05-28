using FamilyTree.Shared.Enums;

namespace FamilyTree.Api.Models;

public partial class Person
{
    public Guid Id { get; set; }

    public string FirstName { get; set; } = null!;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = null!;
    public string? MaidenName { get; set; }

    public DateOnly? BirthDate { get; set; }
    public string? BirthPlace { get; set; }
    public DateOnly? DeathDate { get; set; }
    public string? DeathPlace { get; set; }

    public string? BiographyNotes { get; set; }
    public string? ProfilePhotoUrl { get; set; }
    public Gender? Gender { get; set; }

    public DateTime? CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public byte[]? RowVersion { get; set; }

    // CORRECT: One-to-many
    public virtual ICollection<Medium> Media { get; set; } = new List<Medium>();

    public virtual ICollection<Relationship> RelationshipPersonAs { get; set; } = new List<Relationship>();
    public virtual ICollection<Relationship> RelationshipPersonBs { get; set; } = new List<Relationship>();
}