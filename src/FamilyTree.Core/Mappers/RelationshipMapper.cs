using FamilyTree.Core.Models;
using FamilyTree.Shared.DTOs.Relationship;

namespace FamilyTree.Core.Mappers;

/// <summary>
/// Maps Relationship entities to DTOs.
/// This mapper is intentionally one‑way: the API receives RelationshipUpsertDto
/// and returns RelationshipDto. No tuples, no null warnings.
/// </summary>
public static class RelationshipMapper
{
    /// <summary>
    /// Convert a Relationship EF entity into a RelationshipDto.
    /// </summary>
    public static RelationshipDto MapRelationshipToDto(Relationship r)
    {
        if (r is null)
            throw new ArgumentNullException(nameof(r));

        return new RelationshipDto
        {
            Id = r.Id,
            PersonAId = r.PersonAId,
            PersonBId = r.PersonBId,
            Type = r.Type,
            StartDate = r.StartDate,
            EndDate = r.EndDate,
            Notes = r.Notes ?? string.Empty,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        };
    }

    /// <summary>
    /// Canonical ordering helper for spouse relationships.
    /// Ensures (A,B) is always sorted by Guid to avoid duplicates.
    /// </summary>
    public static (Guid A, Guid B) CanonicalPair(Guid x, Guid y)
    {
        var ordered = new[] { x, y }.OrderBy(g => g).ToArray();
        return (ordered[0], ordered[1]);
    }
}