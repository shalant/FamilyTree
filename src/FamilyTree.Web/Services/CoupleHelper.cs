using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Person;

namespace FamilyTree.Web.Services;

public static class CoupleHelper
{
    public static List<CoupleDto> Derive(List<PersonDto> people)
    {
        var coupleMap = new Dictionary<(Guid, Guid), List<Guid>>();

        // Couples inferred from shared children
        foreach (var person in people)
        {
            if (person.ParentIds is null || person.ParentIds.Count < 2) continue;
            for (int i = 0; i < person.ParentIds.Count - 1; i++)
                for (int j = i + 1; j < person.ParentIds.Count; j++)
                {
                    var a = person.ParentIds[i];
                    var b = person.ParentIds[j];
                    var key = a.CompareTo(b) < 0 ? (a, b) : (b, a);
                    if (!coupleMap.ContainsKey(key)) coupleMap[key] = [];
                    coupleMap[key].Add(person.Id);
                }
        }

        // Couples from explicit spouse relationships (childless couples still need an arc)
        var personIds = people.Select(p => p.Id).ToHashSet();
        foreach (var person in people)
        {
            foreach (var spouseId in person.SpouseIds ?? [])
            {
                if (!personIds.Contains(spouseId)) continue;
                var key = person.Id.CompareTo(spouseId) < 0
                    ? (person.Id, spouseId)
                    : (spouseId, person.Id);
                if (!coupleMap.ContainsKey(key))
                    coupleMap[key] = [];
            }
        }

        return coupleMap.Select(kvp => new CoupleDto
        {
            PersonAId = kvp.Key.Item1,
            PersonBId = kvp.Key.Item2,
            ChildIds = kvp.Value
        }).ToList();
    }
}