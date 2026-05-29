using System.ComponentModel.DataAnnotations;
using FamilyTree.Shared.Enums;

namespace FamilyTree.Shared.DTOs.Relationship;

public class RelationshipUpsertDto
{
    [Required(ErrorMessage = "Person A ID is required.")]
    public Guid PersonAId { get; set; }

    [Required(ErrorMessage = "Person B ID is required.")]
    public Guid PersonBId { get; set; }

    [Required(ErrorMessage = "Relationship type is required.")]
    public RelationshipType Type { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters.")]
    public string? Notes { get; set; }
}