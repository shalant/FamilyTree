using System.Globalization;
using System.Text;
using FamilyTree.Shared.DTOs.Import;
using FamilyTree.Shared.DTOs.Person;

namespace FamilyTree.Core.Services;

/// <summary>
/// Pure (no DB) entity-resolution helper. Given one freshly-imported person and the
/// pool of people already in the family, it suggests likely existing matches so the
/// user can LINK instead of creating a duplicate. Always suggestion-only — it returns
/// candidates, never decides.
///
/// Strategy: fuzzy first+last name (diacritic-stripped, short edit-distance tolerant so
/// Sara/Sarah and Sady/Sadie match), aliases and maiden names treated as extra name keys,
/// weighted with birth-year proximity. A hard year conflict (both known, far apart) rules a
/// pair out even when the names match — same name + different decade = different person.
/// </summary>
public static class ImportMatchService
{
    // Tunables for the "name + birth year, fuzzy-tolerant" profile.
    private const double NameFloor      = 0.72; // min first AND last similarity to be a candidate
    private const double ScoreFloor     = 0.62; // min combined score to surface
    private const int    YearHardCutoff = 6;    // both years known & |diff| > this ⇒ exclude
    private const int    MaxCandidates  = 3;

    public static List<MatchCandidate> FindCandidates(ImportPersonDto imported, IReadOnlyList<PersonDto> existing)
    {
        var impFirsts = NameKeys(imported.FirstName, imported.Aliases);
        var impLasts  = NameKeys(imported.LastName, imported.MaidenName is null ? null : [imported.MaidenName]);
        var impYear   = ParseYear(imported.BirthDate);
        var impGender = Norm(imported.Gender);

        var results = new List<MatchCandidate>();

        foreach (var p in existing)
        {
            var exFirsts = NameKeys(p.FirstName, null);
            var exLasts  = NameKeys(p.LastName, p.MaidenName is null ? null : [p.MaidenName]);

            var firstSim = BestSim(impFirsts, exFirsts);
            var lastSim  = BestSim(impLasts, exLasts);
            if (firstSim < NameFloor || lastSim < NameFloor) continue;

            // Birth-year proximity (and hard conflict exclusion).
            var exYear = p.BirthDate?.Year;
            double yearScore;
            if (impYear is int iy && exYear is int ey)
            {
                var diff = Math.Abs(iy - ey);
                if (diff > YearHardCutoff) continue;            // different person
                yearScore = diff switch { 0 => 1.0, <= 2 => 0.7, _ => 0.35 };
            }
            else
            {
                yearScore = 0.5;                                // unknown — can't disambiguate
            }

            var nameScore = (firstSim + lastSim) / 2.0;
            var score = nameScore * 0.7 + yearScore * 0.3;

            // Gender mismatch (both known) is a soft penalty, not a veto (Sr./Jr., data errors).
            var exGender = Norm(p.Gender?.ToString());
            if (impGender is "male" or "female" && exGender is "male" or "female" && impGender != exGender)
                score *= 0.6;

            if (score < ScoreFloor) continue;

            results.Add(new MatchCandidate
            {
                PersonId    = p.Id,
                DisplayName = p.FullName,
                BirthYear   = exYear,
                Score       = Math.Round(score, 3),
            });
        }

        return results
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.DisplayName)
            .Take(MaxCandidates)
            .ToList();
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static List<string> NameKeys(string? primary, string[]? extras)
    {
        var keys = new List<string>();
        void Add(string? s) { var n = Norm(s); if (n.Length > 0 && !keys.Contains(n)) keys.Add(n); }
        Add(primary);
        if (extras != null) foreach (var e in extras) Add(e);
        return keys;
    }

    private static double BestSim(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var best = 0.0;
        foreach (var x in a)
            foreach (var y in b)
                best = Math.Max(best, Sim(x, y));
        return best;
    }

    private static double Sim(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;
        var dist = Levenshtein(a, b);
        var max  = Math.Max(a.Length, b.Length);
        return 1.0 - (double)dist / max;
    }

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    private static int? ParseYear(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length < 4) return null;
        return int.TryParse(raw[..4], out var y) && y is > 1000 and < 2100 ? y : null;
    }

    private static string Norm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var formD = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark && char.IsLetter(ch))
                sb.Append(ch);
        return sb.ToString();
    }
}
