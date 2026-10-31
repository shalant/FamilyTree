using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Shared.Enums;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Core.Tests;

// B8 (2026-10-27): the Data check's rules. Each rule has a case that fires and a nearby one
// that doesn't, so a rule that fires on everything (or nothing) fails here.
public class DataCheckRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 27);
    private static readonly DateTime LongAgo = new(2020, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly List<Person> _people = [];
    private readonly List<Relationship> _links = [];

    private Person P(string first, string last, int? born = null, int? died = null, Gender? gender = null,
        DateOnly? birthDate = null, DateOnly? deathDate = null, DateTime? created = null, string? maiden = null)
    {
        var p = new Person
        {
            Id = Guid.NewGuid(), FirstName = first, LastName = last, MaidenName = maiden, Gender = gender,
            BirthDate = birthDate ?? (born is { } b ? new DateOnly(b, 6, 1) : null),
            DeathDate = deathDate ?? (died is { } d ? new DateOnly(d, 6, 1) : null),
            CreatedAt = created ?? LongAgo,
        };
        _people.Add(p);
        return p;
    }

    private Relationship Link(Person a, Person b, RelationshipType type,
        DateOnly? start = null, DateOnly? end = null, DateTime? created = null)
    {
        var r = new Relationship
        {
            Id = Guid.NewGuid(), PersonAId = a.Id, PersonBId = b.Id, Type = type,
            StartDate = start, EndDate = end, CreatedAt = created ?? LongAgo,
        };
        _links.Add(r);
        return r;
    }

    private void Parent(Person parent, Person child) => Link(parent, child, RelationshipType.Parent);

    private List<DataCheckFinding> Run() => DataCheckRules.Find(_people, _links, Today);
    private List<DataCheckFinding> Run(DataCheckRule rule) => [.. Run().Where(f => f.Rule == rule)];

    [Fact]
    public void ACleanFamily_HasNoFindings()
    {
        var dad = P("Ray", "Small", 1920, 1990, Gender.Male);
        var mom = P("Rose", "Small", 1922, 2000, Gender.Female);
        var kid = P("Bill", "Small", 1950);
        var kid2 = P("Ann", "Small", 1953);
        Link(dad, mom, RelationshipType.Spouse, start: new DateOnly(1945, 1, 1));
        Parent(dad, kid); Parent(mom, kid); Parent(dad, kid2); Parent(mom, kid2);

        Run().Should().BeEmpty();
    }

    // ── Dates ───────────────────────────────────────────────────────────────────

    [Fact]
    public void DeathBeforeBirth_IsImpossible()
    {
        P("Max", "Fine", 1950, 1940);
        Run(DataCheckRule.Dates).Should().ContainSingle()
            .Which.Should().Match<DataCheckFinding>(f => f.IsImpossible && f.Message.Contains("Max Fine"));
    }

    [Fact]
    public void BirthInTheFuture_IsImpossible()
    {
        P("Max", "Fine", birthDate: Today.AddDays(5));
        Run(DataCheckRule.Dates).Should().ContainSingle().Which.IsImpossible.Should().BeTrue();
    }

    [Fact]
    public void NoDeathDateAndOver110_IsAWarning_ButUnder110IsNot()
    {
        P("Old", "Timer", 1900);
        P("Young", "Timer", 1930);
        Run(DataCheckRule.Dates).Should().ContainSingle()
            .Which.Should().Match<DataCheckFinding>(f => !f.IsImpossible && f.Message.Contains("Old Timer"));
    }

    // ── Parents, ancestors ──────────────────────────────────────────────────────

    [Fact]
    public void ParentYoungerThanChild_IsImpossible()
    {
        Parent(P("Kid", "Moe", 1980), P("Pop", "Moe", 1950));
        Run(DataCheckRule.ParentChild).Should().ContainSingle().Which.IsImpossible.Should().BeTrue();
    }

    [Fact]
    public void MotherOver55_IsAWarning()
    {
        Parent(P("Ma", "Moe", 1900, gender: Gender.Female), P("Kid", "Moe", 1960));
        Run(DataCheckRule.ParentChild).Should().ContainSingle().Which.IsImpossible.Should().BeFalse();
    }

    [Fact]
    public void GrandparentOnly20YearsOlderWithParentUndated_IsAWarning()
    {
        var gp = P("Gran", "Cole", 1950);
        var mid = P("Mid", "Cole");
        var kid = P("Kid", "Cole", 1970);
        Parent(gp, mid); Parent(mid, kid);

        var f = Run(DataCheckRule.Ancestor).Should().ContainSingle().Subject;
        f.IsImpossible.Should().BeFalse();
        f.PersonIds.Should().BeEquivalentTo([gp.Id, kid.Id]);
    }

    [Fact]
    public void Grandparent30YearsOlderWithParentUndated_IsFine()
    {
        var gp = P("Gran", "Cole", 1940);
        var mid = P("Mid", "Cole");
        Parent(gp, mid); Parent(mid, P("Kid", "Cole", 1970));
        Run(DataCheckRule.Ancestor).Should().BeEmpty();
    }

    [Fact]
    public void GrandparentBornAfterGrandchild_IsImpossible()
    {
        var gp = P("Gran", "Cole", 1980);
        var mid = P("Mid", "Cole");
        Parent(gp, mid); Parent(mid, P("Kid", "Cole", 1891));
        Run(DataCheckRule.Ancestor).Should().ContainSingle().Which.IsImpossible.Should().BeTrue();
    }

    // A dated person in between moves the finding onto a parent link, so it isn't
    // reported twice.
    [Fact]
    public void AncestorPair_WithADatedPersonBetween_IsLeftToTheParentLinks()
    {
        var gp = P("Gran", "Cole", 1950);
        var mid = P("Mid", "Cole", 1960);
        Parent(gp, mid); Parent(mid, P("Kid", "Cole", 1965));
        Run(DataCheckRule.Ancestor).Should().BeEmpty();
        Run(DataCheckRule.ParentChild).Should().HaveCount(2);
    }

    [Fact]
    public void AParentLoop_DoesNotHang()
    {
        var a = P("Al", "Loop", 1900);
        var b = P("Bo", "Loop");
        var c = P("Cy", "Loop");
        Parent(a, b); Parent(b, c); Parent(c, a);
        var act = () => Run();
        act.Should().NotThrow();
    }

    // ── Marriages ───────────────────────────────────────────────────────────────

    [Fact]
    public void MarriageEndingBeforeItStarts_IsAWarning()
    {
        Link(P("Al", "Ames", 1900), P("Bea", "Ames", 1902), RelationshipType.Spouse,
            start: new DateOnly(1930, 1, 1), end: new DateOnly(1925, 1, 1));
        Run(DataCheckRule.Marriage).Should().ContainSingle()
            .Which.Should().Match<DataCheckFinding>(f => !f.IsImpossible && f.Message.Contains("ends"));
    }

    [Fact]
    public void MarriageStartingAfterAPartnerDied_IsAWarning()
    {
        Link(P("Al", "Ames", 1900, 1920), P("Bea", "Ames", 1902), RelationshipType.Spouse, start: new DateOnly(1930, 1, 1));
        Run(DataCheckRule.Marriage).Should().ContainSingle().Which.Message.Should().Contain("after Al Ames died");
    }

    // The app sets an ended marriage's EndDate to the earlier partner's death date; that
    // must not be flagged.
    [Fact]
    public void MarriageEndingOnTheEarlierDeath_IsFine()
    {
        var al = P("Al", "Ames", 1900, 1960);
        var bea = P("Bea", "Ames", 1902, 1980);
        Link(al, bea, RelationshipType.Spouse, start: new DateOnly(1925, 1, 1), end: al.DeathDate);
        Run(DataCheckRule.Marriage).Should().BeEmpty();
    }

    [Fact]
    public void MarriedToSomeoneWhoDiedBeforeTheyWereBorn_IsAWarning()
    {
        Link(P("Al", "Ames", 1850, 1890), P("Bea", "Ames", 1900), RelationshipType.Spouse);
        Run(DataCheckRule.Marriage).Should().ContainSingle().Which.Message.Should().Contain("before Bea Ames was born");
    }

    [Fact]
    public void MarryingAt10_IsAWarning()
    {
        Link(P("Al", "Ames", 1900), P("Bea", "Ames", 1920), RelationshipType.Spouse, start: new DateOnly(1930, 7, 1));
        Run(DataCheckRule.Marriage).Should().ContainSingle().Which.Message.Should().Contain("Bea Ames would have been 10");
    }

    // ── Siblings and role clashes ───────────────────────────────────────────────

    [Fact]
    public void LinkedSiblingsBornGenerationsApart_IsAWarning_But30YearsIsNot()
    {
        Link(P("Old", "Sib", 1896), P("New", "Sib", 1970), RelationshipType.Sibling);
        Link(P("Half", "Sib", 1940), P("Late", "Sib", 1970), RelationshipType.Sibling);
        Run(DataCheckRule.Siblings).Should().ContainSingle().Which.Message.Should().Contain("Old Sib");
    }

    [Fact]
    public void ParentWhoIsAlsoASpouse_IsImpossible()
    {
        var a = P("Al", "Clash");
        var b = P("Bo", "Clash");
        Parent(a, b);
        Link(a, b, RelationshipType.Spouse);
        Run(DataCheckRule.RoleClash).Should().ContainSingle().Which.IsImpossible.Should().BeTrue();
    }

    [Fact]
    public void SiblingWhoIsAlsoANephew_IsAWarning()
    {
        var gran = P("Gran", "Nest");
        var aunt = P("Aunt", "Nest");
        var mom = P("Mom", "Nest");
        var kid = P("Kid", "Nest");
        Parent(gran, aunt); Parent(gran, mom); // aunt and mom are sisters
        Parent(mom, kid);
        Link(kid, aunt, RelationshipType.Sibling); // kid also recorded as aunt's sibling

        Run(DataCheckRule.RoleClash).Should().ContainSingle()
            .Which.Message.Should().Contain("Kid Nest is recorded as both Aunt Nest's sibling and Aunt Nest's niece or nephew");
    }

    // ── Entry day ───────────────────────────────────────────────────────────────

    [Fact]
    public void BirthDateOnTheDayTheRecordWasEntered_IsFlagged_ButAnOrdinaryDateIsNot()
    {
        P("Today", "Slip", birthDate: new DateOnly(2026, 9, 18), created: new DateTime(2026, 9, 18, 21, 0, 0, DateTimeKind.Utc));
        P("Real", "Date", birthDate: new DateOnly(1950, 9, 18), created: new DateTime(2026, 9, 18, 21, 0, 0, DateTimeKind.Utc));
        Run(DataCheckRule.EntryDay).Should().ContainSingle().Which.Message.Should().Contain("Today Slip");
    }

    // Entered in the evening in Chicago is the next day in UTC.
    [Fact]
    public void EntryDay_AllowsADayForTheTimeZone()
    {
        P("Late", "Night", birthDate: new DateOnly(2026, 9, 18), created: new DateTime(2026, 9, 19, 3, 0, 0, DateTimeKind.Utc));
        Run(DataCheckRule.EntryDay).Should().ContainSingle();
    }

    [Fact]
    public void MarriageEndDateOnTheDayTheLinkWasEntered_IsFlagged()
    {
        Link(P("Lillian", "Ross", 1930), P("Louis", "Ross", 1928), RelationshipType.Spouse,
            end: new DateOnly(2026, 9, 18), created: new DateTime(2026, 9, 18, 15, 0, 0, DateTimeKind.Utc));
        Run(DataCheckRule.EntryDay).Should().ContainSingle().Which.Message.Should().Contain("The end of");
    }

    // ── Names ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ParentAndChildWithSurnamesOneLetterApart_AreFlagged()
    {
        Parent(P("Sam", "Schmulevith", 1900), P("Ida", "Schumevith", 1930));
        Run(DataCheckRule.Surname).Should().ContainSingle().Which.Message.Should().Contain("two letters");
    }

    // Found on the live run, 2026-10-28: "Mell" and "Small" are two letters apart, which
    // on names that short is most of the name, not a typo.
    [Fact]
    [Trait("Category", "Regression")]
    public void ShortSurnamesTwoLettersApart_AreNotFlagged()
    {
        Parent(P("Lillian", "Mell", 1930), P("Ray", "Small", 1955));
        Run(DataCheckRule.Surname).Should().BeEmpty();
    }

    [Fact]
    public void ShortSurnamesOneLetterApart_AreFlagged()
    {
        Parent(P("Sam", "Small", 1900), P("Ida", "Smal", 1930));
        Run(DataCheckRule.Surname).Should().ContainSingle().Which.Message.Should().Contain("one letter");
    }

    [Fact]
    public void SpousesWithSimilarSurnames_AreNotFlagged()
    {
        Link(P("Al", "Rosen", 1900), P("Bea", "Rosel", 1902), RelationshipType.Spouse);
        Run(DataCheckRule.Surname).Should().BeEmpty();
    }

    [Fact]
    public void BillAndWilliamWithTheSameSurname_AreAPossibleDuplicate()
    {
        P("William", "Small", 1925);
        P("Bill", "Small");
        Run(DataCheckRule.Duplicate).Should().ContainSingle();
    }

    [Fact]
    public void SameNameBornYearsApart_IsNotADuplicate()
    {
        P("John", "Small", 1900);
        P("John", "Small", 1960);
        Run(DataCheckRule.Duplicate).Should().BeEmpty();
    }

    [Fact]
    public void SameNameFatherAndSon_AreNotADuplicate()
    {
        Parent(P("John", "Small"), P("John", "Small"));
        Run(DataCheckRule.Duplicate).Should().BeEmpty();
    }

    [Fact]
    public void ShortNamesALetterApart_AreNotADuplicate()
    {
        P("Max", "Fleishman");
        P("Mae", "Fleishman");
        Run(DataCheckRule.Duplicate).Should().BeEmpty();
    }

    // Found by DataCheckFixtureTests on deep-ancestor-line: Johann and Johanna Weidmann.
    [Theory]
    [InlineData("Johann", "Johanna")]
    [InlineData("Louis", "Louise")]
    [InlineData("Paul", "Paula")]
    public void ANameAndItsFeminineForm_AreNotADuplicate(string a, string b)
    {
        P(a, "Weidmann");
        P(b, "Weidmann");
        Run(DataCheckRule.Duplicate).Should().BeEmpty();
    }

    [Fact]
    public void OneLetterApartInALongerName_IsAPossibleDuplicate()
    {
        P("Elliot", "Rosenberg");
        P("Eliot", "Rosenberg");
        Run(DataCheckRule.Duplicate).Should().ContainSingle();
    }

    [Fact]
    public void SameNameButDifferentRecordedGenders_IsNotADuplicate()
    {
        P("Jordan", "Small", gender: Gender.Male);
        P("Jordan", "Small", gender: Gender.Female);
        Run(DataCheckRule.Duplicate).Should().BeEmpty();
    }

    [Fact]
    public void MaidenNameMatchingTheOtherRecordsSurname_IsAPossibleDuplicate()
    {
        P("Florence", "Fleishman", maiden: "Herskovitz");
        P("Florence", "Herskovitz");
        Run(DataCheckRule.Duplicate).Should().ContainSingle();
    }

    // ── Scope ───────────────────────────────────────────────────────────────────

    [Fact]
    public void LinksToPeopleNotGiven_AreIgnored()
    {
        var kid = P("Kid", "Here", 1980);
        var outsider = new Person { Id = Guid.NewGuid(), FirstName = "Out", LastName = "Sider", BirthDate = new DateOnly(1990, 1, 1) };
        Link(outsider, kid, RelationshipType.Parent);
        Run().Should().BeEmpty();
    }
}
