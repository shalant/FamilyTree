using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Person;

namespace FamilyTree.Web.Services;

public static class CoupleHelper
{
    public static List<CoupleDto> Derive(List<PersonDto> people)
    {
        var coupleMap = new Dictionary<(Guid, Guid), List<Guid>>();
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
        return coupleMap.Select(kvp => new CoupleDto
        {
            PersonAId = kvp.Key.Item1,
            PersonBId = kvp.Key.Item2,
            ChildIds = kvp.Value
        }).ToList();
    }
}