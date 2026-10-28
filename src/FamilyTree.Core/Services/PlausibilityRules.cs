using FamilyTree.Shared.Enums;

namespace FamilyTree.Core.Services;

/// <summary>A person's name, dates and gender: all the plausibility rules look at.</summary>
public sealed record LifeDates(string Name, DateOnly? BirthDate, DateOnly? DeathDate, Gender? Gender);

public enum PlausibilitySeverity
{
    /// <summary>Impossible: the server refuses the save.</summary>
    Error,
    /// <summary>Unlikely but possible: the form asks "are you sure?" and the server allows it.</summary>
    Warning,
}

public sealed record PlausibilityFinding(PlausibilitySeverity Severity, string Message);

/// <summary>
/// Entry guardrails (Outreach readiness A3, 2026-10-25). One set of rules for dates that
/// can't be right and dates that are probably a slip, so the server's refusals and the
/// form's warnings never disagree. <see cref="PersonService"/> and
/// <see cref="RelationshipService"/> refuse <see cref="PlausibilitySeverity.Error"/>s;
/// <c>PersonForm</c> shows <see cref="PlausibilitySeverity.Warning"/>s and saves only once
/// the user confirms. Before this, the parent-age and posthumous-birth limits were hard
/// errors duplicated in both services, with nothing between "fine" and "refused".
///
/// Only biological parent links are checked: an adoptive parent can be any age, and could
/// adopt after a death in the family.
/// </summary>
public static class PlausibilityRules
{
    public const int MinParentAge = 12;
    public const int MaxMotherAge = 55;
    public const int MaxParentAge = 80;
    public const int MaxLifespan = 110;
    public const int MinMarriageAge = 12;
    public const int MaxSiblingGap = 50;
    public static readonly DateOnly EarliestBirth = new(1800, 1, 1);

    /// <summary>
    /// A person's own dates, refused by <see cref="PersonService"/> on every save. Moved here
    /// from PersonService (B8, 2026-10-27) so the Data check can find rows that slipped past
    /// it (an older import path, a direct database edit).
    /// </summary>
    public static IEnumerable<PlausibilityFinding> CheckRecordedDates(DateOnly? birth, DateOnly? death, DateOnly today)
    {
        if (birth > today) yield return Error("Birth date cannot be in the future.");
        if (birth < EarliestBirth) yield return Error("Birth date cannot be before 1800.");
        if (death > today) yield return Error("Death date cannot be in the future.");
        if (birth.HasValue && death.HasValue && birth.Value >= death.Value)
            yield return Error("Birth date must be before death date.");
    }

    public static IEnumerable<PlausibilityFinding> CheckPerson(LifeDates person, DateOnly today)
    {
        if (person.BirthDate is not { } born) yield break;

        if (person.DeathDate is { } died)
        {
            var ageAtDeath = AgeOn(born, died);
            if (ageAtDeath > MaxLifespan)
                yield return Warn($"{person.Name} would have been {ageAtDeath} when they died.");
        }
        else
        {
            var age = AgeOn(born, today);
            if (age > MaxLifespan)
                yield return Warn($"{person.Name} would be {age} today. If they have died, add a death date.");
        }
    }

    public static IEnumerable<PlausibilityFinding> CheckParentChild(LifeDates parent, LifeDates child)
    {
        if (child.BirthDate is not { } childBorn) yield break;

        if (parent.BirthDate is { } parentBorn)
        {
            if (parentBorn >= childBorn)
            {
                yield return Error($"{parent.Name} was born on {Format(parentBorn)}, not before {child.Name} " +
                                   $"({Format(childBorn)}), so can't be {child.Name}'s parent.");
                yield break;
            }

            var age = AgeOn(parentBorn, childBorn);
            if (age < MinParentAge)
                yield return Warn($"{parent.Name} would have been {age} when {child.Name} was born.");
            else if (age > MaxParentAge)
                yield return Warn($"{parent.Name} would have been {age} when {child.Name} was born.");
            else if (parent.Gender == Gender.Female && age > MaxMotherAge)
                yield return Warn($"{parent.Name} would have been {age} when she gave birth to {child.Name}.");
        }

        if (parent.DeathDate is { } parentDied && childBorn > parentDied)
        {
            // A mother can't give birth after her death. A father can die before the birth,
            // but not much more than nine months before; a year leaves room for a date
            // that's only roughly right. Gender unknown: could be either, so no finding
            // inside the year.
            if (parent.Gender == Gender.Female)
                yield return Error($"{parent.Name} died on {Format(parentDied)}, before {child.Name} was born " +
                                   $"({Format(childBorn)}), so can't be {child.Name}'s mother.");
            else if (childBorn > parentDied.AddYears(1))
                yield return Warn($"{parent.Name} died on {Format(parentDied)}, more than a year before " +
                                  $"{child.Name} was born ({Format(childBorn)}).");
        }
    }

    /// <summary>
    /// An ancestor two or more generations up must be born before the descendant. Found by
    /// the write-path fuzz harness (B7, 2026-10-25): with the person in between dateless,
    /// <see cref="CheckParentChild"/> has nothing to compare on either link, so a grandparent
    /// born in 1980 of a grandchild born in 1891 was saved, and the tree drew the dateless
    /// parent above their own parent.
    /// </summary>
    public static PlausibilityFinding? CheckAncestor(LifeDates ancestor, LifeDates descendant, int generations)
    {
        if (ancestor.BirthDate is not { } a || descendant.BirthDate is not { } d || a < d) return null;
        var kin = generations switch { 2 => "grandparent", 3 => "great-grandparent", _ => "ancestor" };
        return Error($"{ancestor.Name} was born on {Format(a)}, not before {descendant.Name} ({Format(d)}), " +
                     $"so can't be {descendant.Name}'s {kin}. Check the parents and children in between.");
    }

    /// <summary>
    /// An ancestor two or more generations up with nobody dated in between must be at least
    /// <see cref="MinParentAge"/> years older per generation. Allowed (B7, 2026-10-25) but
    /// drawn badly, because no row fits the undated people in between. Warnings only.
    /// </summary>
    public static PlausibilityFinding? CheckAncestorGap(LifeDates ancestor, LifeDates descendant, int generations)
    {
        if (ancestor.BirthDate is not { } a || descendant.BirthDate is not { } d || a >= d) return null;
        var gap = AgeOn(a, d);
        if (gap >= MinParentAge * generations) return null;
        var kin = generations switch { 2 => "grandparent", 3 => "great-grandparent", _ => "ancestor" };
        return Warn($"{ancestor.Name} would have been {gap} when {descendant.Name} was born, but is recorded as " +
                    $"{descendant.Name}'s {kin}. Check the parents and children in between.");
    }

    /// <summary>
    /// A marriage's dates against its partners' lives (B8, 2026-10-27). Warnings only for now:
    /// the Data check reports them; the write path and the form don't use them yet.
    /// </summary>
    public static IEnumerable<PlausibilityFinding> CheckMarriage(
        LifeDates a, LifeDates b, DateOnly? start, DateOnly? end)
    {
        // Someone married to a partner who died before they were born (B7).
        foreach (var (p, q) in new[] { (a, b), (b, a) })
            if (p.DeathDate is { } died && q.BirthDate is { } born && died < born)
                yield return Warn($"{p.Name} died on {Format(died)}, before {q.Name} was born ({Format(born)}), " +
                                  $"but they're recorded as married.");

        if (start is { } s && end is { } e && e < s)
            yield return Warn($"The marriage of {a.Name} and {b.Name} ends ({Format(e)}) before it starts ({Format(s)}).");

        foreach (var p in new[] { a, b })
        {
            if (start is { } st && p.BirthDate is { } born)
            {
                if (st < born)
                    yield return Warn($"The marriage of {a.Name} and {b.Name} starts ({Format(st)}) before " +
                                      $"{p.Name} was born ({Format(born)}).");
                else if (AgeOn(born, st) < MinMarriageAge)
                    yield return Warn($"{p.Name} would have been {AgeOn(born, st)} when they married.");
            }
            if (start is { } st2 && p.DeathDate is { } died && st2 > died)
                yield return Warn($"The marriage of {a.Name} and {b.Name} starts ({Format(st2)}) after " +
                                  $"{p.Name} died ({Format(died)}).");
            if (end is { } en && p.DeathDate is { } died2 && en > died2)
                yield return Warn($"The marriage of {a.Name} and {b.Name} ends ({Format(en)}) after " +
                                  $"{p.Name} died ({Format(died2)}).");
        }
    }

    /// <summary>Recorded siblings born generations apart (B7: a sibling born 1896 of someone
    /// whose own child was born 2005). Allowed but drawn badly.</summary>
    public static PlausibilityFinding? CheckSiblings(LifeDates a, LifeDates b)
    {
        if (a.BirthDate is not { } x || b.BirthDate is not { } y) return null;
        var (older, younger, ob, yb) = x <= y ? (a, b, x, y) : (b, a, y, x);
        var gap = AgeOn(ob, yb);
        return gap > MaxSiblingGap
            ? Warn($"{older.Name} and {younger.Name} are recorded as siblings but were born {gap} years apart.")
            : null;
    }

    /// <summary>Whole years from <paramref name="birth"/> to <paramref name="on"/>.</summary>
    public static int AgeOn(DateOnly birth, DateOnly on)
    {
        var age = on.Year - birth.Year;
        if (on < birth.AddYears(age)) age--;
        return age;
    }

    private static string Format(DateOnly d) => d.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

    private static PlausibilityFinding Error(string message) => new(PlausibilitySeverity.Error, message);
    private static PlausibilityFinding Warn(string message) => new(PlausibilitySeverity.Warning, message);
}
