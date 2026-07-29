using FamilyTree.Shared.DTOs.Person;

namespace FamilyTree.Web.Modules.Components;

// Extracted out of LinkToTreeModal.razor so the matching rule itself is unit-testable without
// bUnit/MudBlazor rendering. Supports both exact and fuzzy name matching — exact matches are
// prioritized first, then fuzzy matches meeting a 80% similarity threshold (Levenshtein distance).
// This catches the actual failure mode (exact duplicates like "Elliot Rosenberg" typed twice) while
// also catching likely typos or common nicknames ("John" vs "Jon", "Bill" vs "William").
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
                var similarity = CalculateSimilarity(inputFullName, personFullName);
                return (Person: p, Similarity: similarity);
            })
            .Where(m => m.Similarity >= MinimumSimilarityThreshold)
            .OrderByDescending(m => m.Similarity)
            .Select(m => m.Person)
            .ToList();

        return fuzzyMatches;
    }

    // Levenshtein distance similarity: returns a value between 0 (completely different) and 1 (identical).
    private static double CalculateSimilarity(string s1, string s2)
    {
        if (s1.Length == 0 && s2.Length == 0) return 1.0;
        if (s1.Length == 0 || s2.Length == 0) return 0.0;

        var distance = LevenshteinDistance(s1.ToLower(), s2.ToLower());
        var maxLength = Math.Max(s1.Length, s2.Length);
        return 1.0 - (double)distance / maxLength;
    }

    // Standard Levenshtein distance algorithm: minimum number of single-character edits
    // (insertions, deletions, substitutions) needed to transform one string into another.
    private static int LevenshteinDistance(string s1, string s2)
    {
        var m = s1.Length;
        var n = s2.Length;
        var dp = new int[m + 1, n + 1];

        for (int i = 0; i <= m; i++)
            dp[i, 0] = i;

        for (int j = 0; j <= n; j++)
            dp[0, j] = j;

        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                if (s1[i - 1] == s2[j - 1])
                {
                    dp[i, j] = dp[i - 1, j - 1];
                }
                else
                {
                    dp[i, j] = 1 + Math.Min(
                        Math.Min(dp[i - 1, j], dp[i, j - 1]),
                        dp[i - 1, j - 1]);
                }
            }
        }

        return dp[m, n];
    }
}
