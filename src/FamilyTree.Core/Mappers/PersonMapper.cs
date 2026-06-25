using FamilyTree.Core.Models;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.Enums;
using FamilyTree.Shared.Enums;

namespace FamilyTree.Core.Mappers;

public static class PersonMapper
{
    public static PersonDto MapPersonToDto(Person person, IEnumerable<Relationship> relationships)
    {
        var rels = relationships.ToList();

        // ─────────────────────────────────────────────────────────────
        // PARENTS (A = parent, B = child)
        // ─────────────────────────────────────────────────────────────
        var parentIds = rels
            .Where(r => r.Type == RelationshipType.Parent &&
                        r.PersonBId == person.Id)
            .Select(r => r.PersonAId)
            .Distinct()
            .ToList();

        // ─────────────────────────────────────────────────────────────
        // CHILDREN (A = parent, B = child)
        // ─────────────────────────────────────────────────────────────
        var childIds = rels
            .Where(r => r.Type == RelationshipType.Parent &&
                        r.PersonAId == person.Id)
            .Select(r => r.PersonBId)
            .Distinct()
            .ToList();

        // ─────────────────────────────────────────────────────────────
        // SPOUSES — active (EndDate = null) and former (EndDate set)
        // ─────────────────────────────────────────────────────────────
        var spouseRels = rels
            .Where(r => r.Type == RelationshipType.Spouse &&
                       (r.PersonAId == person.Id || r.PersonBId == person.Id));

        var spouseRelsList = spouseRels.ToList();

        var spouseIds = spouseRelsList
            .Where(r => r.EndDate == null)
            .Select(r => r.PersonAId == person.Id ? r.PersonBId : r.PersonAId)
            .Distinct()
            .ToList();

        var formerSpouseIds = spouseRelsList
            .Where(r => r.EndDate != null)
            .Select(r => r.PersonAId == person.Id ? r.PersonBId : r.PersonAId)
            .Distinct()
            .ToList();

        var spouseDates = spouseRelsList
            .GroupBy(r => r.PersonAId == person.Id ? r.PersonBId : r.PersonAId)
            .ToDictionary(
                g => g.Key,
                g => { var r = g.First(); return new SpouseMarriageDates(r.StartDate, r.EndDate); });

        // ─────────────────────────────────────────────────────────────
        // SIBLINGS — EXPLICIT (RelationshipType.Sibling)
        // ─────────────────────────────────────────────────────────────
        var directSiblingIds = rels
            .Where(r => r.Type == RelationshipType.Sibling &&
                       (r.PersonAId == person.Id || r.PersonBId == person.Id))
            .Select(r => r.PersonAId == person.Id ? r.PersonBId : r.PersonAId)
            .Distinct()
            .ToList();

        // ─────────────────────────────────────────────────────────────
        // SIBLINGS — INFERRED (children of the same parents)
        // ─────────────────────────────────────────────────────────────
        var inferredSiblingIds = parentIds
            .SelectMany(pid =>
                rels.Where(r => r.Type == RelationshipType.Parent &&
                                r.PersonAId == pid)
                    .Select(r => r.PersonBId))
            .Where(id => id != person.Id)
            .Distinct()
            .ToList();

        // ─────────────────────────────────────────────────────────────
        // MERGE EXPLICIT + INFERRED
        // ─────────────────────────────────────────────────────────────
        var siblingIds = directSiblingIds
            .Concat(inferredSiblingIds)
            .Distinct()
            .ToList();

        // ─────────────────────────────────────────────────────────────
        // BUILD DTO
        // ─────────────────────────────────────────────────────────────
        return new PersonDto
        {
            Id = person.Id,
            FirstName = person.FirstName,
            MiddleName = person.MiddleName,
            LastName = person.LastName,
            MaidenName = person.MaidenName,

            BirthDate = person.BirthDate,
            BirthPlace = person.BirthPlace,
            DeathDate = person.DeathDate,
            DeathPlace = person.DeathPlace,

            BiographyNotes = person.BiographyNotes,
            ProfilePhotoUrl = person.ProfilePhotoUrl,

            Gender = person.Gender,
            RowVersion = person.RowVersion,
            DeletedAt = person.DeletedAt,

            ParentIds = parentIds,
            ChildIds = childIds,
            SpouseIds = spouseIds,
            FormerSpouseIds = formerSpouseIds,
            SiblingIds = siblingIds,
            SpouseDates = spouseDates
        };
    }
}