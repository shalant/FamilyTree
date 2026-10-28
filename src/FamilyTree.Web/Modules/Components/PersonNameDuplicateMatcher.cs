using FamilyTree.Core.Services;
using FamilyTree.Shared.DTOs.Person;

namespace FamilyTree.Web.Modules.Components;

// Extracted out of LinkToTreeModal.razor so the matching rule itself is unit-testable without
// bUnit/MudBlazor rendering. Supports both exact and fuzzy name matching — exact matches are
// prioritized first, then fuzzy matches meeting a 80% similarity threshold (Levenshtein distance,
// NameSimilarity). This catches the actual failure mode (exact duplicates like "Elliot Rosenberg"
// typed twice) and likely typos ("Jon" vs "John" with a long surname). It does NOT catch
// nicknames: "Bill Small" vs "William Small" scores about 0.6. The Data check's duplicate rule
// (DataCheckRules) adds NameSimilarity.SameFirstName for that.
public static class PersonNameDuplicateMatcher
{
    private const double MinimumSimilarityThreshold = 0.80;

    public static List<PersonDto> FindMatches(IEnumerable<PersonDto> people, string? firstName, string? lastName)
    {
        var first = (firstName ?? "").Trim();
        var last = (lastName ?? "").Trim();
        if (first.Length == 0 && last.Length == 0) return [];

        var peopleList = people.ToList();

        // Exact matches first (case/whitespace-insensitive)
        var exactMatches = peopleList
            .Where(p => string.Equals((p.FirstName ?? "").Trim(), first, StringComparison.OrdinalIgnoreCase)
                     && string.Equals((p.LastName ?? "").Trim(), last, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exactMatches.Count > 0)
            return exactMatches;

        // If no exact match, try fuzzy matching on full names
        if (first.Length == 0 || last.Length == 0)
            return []; // Don't fuzzy-match incomplete names

        var inputFullName = $"{first} {last}".Trim();
        var fuzzyMatches = peopleList
            .Select(p =>
            {
                var personFullName = $"{(p.FirstName ?? "").Trim()} {(p.LastName ?? "").Trim()}".Trim();
                var similarity = NameSimilarity.Similarity(inputFullName, personFullName);
                return (Person: p, Similarity: similarity);
            })
            .Where(m => m.Similarity >= MinimumSimilarityThreshold)
            .OrderByDescending(m => m.Similarity)
            .Select(m => m.Person)
            .ToList();

        return fuzzyMatches;
    }
}
