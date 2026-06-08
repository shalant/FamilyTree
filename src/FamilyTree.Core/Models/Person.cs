using FamilyTree.Shared.Enums;

namespace FamilyTree.Core.Models;

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

    public Guid? FamilyId { get; set; }
    public virtual Family? Family { get; set; }

    public DateTime? CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public byte[]? RowVersion { get; set; }

    // null = derive from BirthDate; true = force minor; false = force adult
    public bool? IsMinorOverride { get; set; }

    // CORRECT: One-to-many
    public virtual ICollection<Medium> Media { get; set; } = new List<Medium>();

    public virtual ICollection<Relationship> RelationshipPersonAs { get; set; } = new List<Relationship>();
    public virtual ICollection<Relationship> RelationshipPersonBs { get; set; } = new List<Relationship>();
}