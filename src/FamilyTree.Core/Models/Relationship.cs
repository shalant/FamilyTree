using FamilyTree.Shared.Enums;

namespace FamilyTree.Core.Models;

public partial class Relationship
{
    public Guid Id { get; set; }

    public Guid PersonAId { get; set; }
    public Guid PersonBId { get; set; }

    public RelationshipType Type { get; set; }
    public string? Notes { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public DateTime? CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public byte[]? RowVersion { get; set; }

    public virtual Person PersonA { get; set; } = null!;
    public virtual Person PersonB { get; set; } = null!;
}