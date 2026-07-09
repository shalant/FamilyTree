using FamilyTree.Shared.DTOs.Person;

namespace FamilyTree.Web.Modules.Components;

// Extracted out of LinkToTreeModal.razor so the matching rule itself is unit-testable without
// bUnit/MudBlazor rendering. Deliberately exact-match only (case/whitespace-insensitive), not
// fuzzy — see TodoList.md: fuzzy name matching was already judged not worth the false-positive
// risk for this flow. This guards against the actual failure mode seen in practice: the exact
// same name ("Elliot Rosenberg") being typed in twice, once as a duplicate Person row.
public static class PersonNameDuplicateMatcher
{
    public static List<PersonDto> FindMatches(IEnumerable<PersonDto> people, string? firstName, string? lastName)
    {
        var first = (firstName ?? "").Trim();
        var last = (lastName ?? "").Trim();
        if (first.Length == 0 && last.Length == 0) return [];

        return people
            .Where(p => string.Equals((p.FirstName ?? "").Trim(), first, StringComparison.OrdinalIgnoreCase)
                     && string.Equals((p.LastName ?? "").Trim(), last, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
