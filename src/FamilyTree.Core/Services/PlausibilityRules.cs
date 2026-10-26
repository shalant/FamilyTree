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
