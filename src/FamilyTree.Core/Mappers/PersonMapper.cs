using FamilyTree.Core.Models;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Shared.Enums;

namespace FamilyTree.Core.Mappers;

public static class PersonMapper
{
    public static PersonDto MapPersonToDto(Person person, IEnumerable<Relationship> relationships)
    {
        var rels = relationships.ToList();

        // Parents (A = parent, B = child)
        var parentIds = rels
            .Where(r => r.Type == RelationshipType.Parent && r.PersonBId == person.Id)
            .Select(r => r.PersonAId)
            .Distinct()
            .ToList();

        // Children (A = parent, B = child)
        var childIds = rels
            .Where(r => r.Type == RelationshipType.Parent && r.PersonAId == person.Id)
            .Select(r => r.PersonBId)
            .Distinct()
            .ToList();

        // Spouses (canonical)
        var spouseIds = rels
            .Where(r => r.Type == RelationshipType.Spouse &&
                       (r.PersonAId == person.Id || r.PersonBId == person.Id))
            .Select(r => r.PersonAId == person.Id ? r.PersonBId : r.PersonAId)
            .Distinct()
            .ToList();

        // Siblings = children of the same parents, excluding self
        var siblingIds = parentIds
            .SelectMany(pid =>
                rels.Where(r => r.Type == RelationshipType.Parent && r.PersonAId == pid)
                    .Select(r => r.PersonBId))
            .Where(id => id != person.Id)
            .Distinct()
            .ToList();

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

            ParentIds = parentIds,
            ChildIds = childIds,
            SpouseIds = spouseIds,
            SiblingIds = siblingIds
        };
    }
}