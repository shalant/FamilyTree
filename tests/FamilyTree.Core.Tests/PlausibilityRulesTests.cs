using FamilyTree.Core.Services;
using FamilyTree.Shared.Enums;
using FluentAssertions;
using Xunit;

namespace FamilyTree.Core.Tests;

// Outreach readiness A3 (2026-10-25): impossible dates are errors, implausible ones warnings.
public class PlausibilityRulesTests
{
    private static readonly DateOnly ChildBorn = new(1950, 6, 15);

    private static LifeDates Child => new("Kid Test", ChildBorn, null, Gender.Unknown);

    private static LifeDates Parent(DateOnly? born, DateOnly? died = null, Gender gender = Gender.Unknown) =>
        new("Parent Test", born, died, gender);

    private static List<PlausibilityFinding> Check(LifeDates parent) =>
        PlausibilityRules.CheckParentChild(parent, Child).ToList();

    [Fact]
    public void ParentBornAfterTheChild_IsAnError()
    {
        Check(Parent(ChildBorn.AddDays(1))).Should().ContainSingle(f => f.Severity == PlausibilitySeverity.Error);
        Check(Parent(ChildBorn)).Should().ContainSingle(f => f.Severity == PlausibilitySeverity.Error);
    }

    [Theory]
    [InlineData(11, true)]
    [InlineData(12, false)]
    [InlineData(80, false)]
    [InlineData(81, true)]
    public void ParentAge_OutsideTwelveToEighty_IsAWarning(int age, bool warned)
    {
        var findings = Check(Parent(ChildBorn.AddYears(-age)));
        findings.Should().NotContain(f => f.Severity == PlausibilitySeverity.Error);
        findings.Any(f => f.Severity == PlausibilitySeverity.Warning).Should().Be(warned);
    }

    [Fact]
    public void ParentAge_UsesTheWholeDates_NotJustTheYears()
    {
        // Born 1938-12-31: still 11 on 1950-06-15, though 1950 - 1938 = 12.
        Check(Parent(new DateOnly(1938, 12, 31))).Should().ContainSingle(f => f.Message.Contains("would have been 11"));
    }

    [Fact]
    public void MotherOverFiftyFive_IsAWarning_ButAFatherIsNot()
    {
        Check(Parent(ChildBorn.AddYears(-56), gender: Gender.Female))
            .Should().ContainSingle(f => f.Severity == PlausibilitySeverity.Warning);
        Check(Parent(ChildBorn.AddYears(-56), gender: Gender.Male)).Should().BeEmpty();
        Check(Parent(ChildBorn.AddYears(-55), gender: Gender.Female)).Should().BeEmpty();
    }

    [Fact]
    public void ChildBornAfterTheMothersDeath_IsAnError_ButOnTheSameDayIsNot()
    {
        Check(Parent(new DateOnly(1920, 1, 1), ChildBorn.AddDays(-1), Gender.Female))
            .Should().ContainSingle(f => f.Severity == PlausibilitySeverity.Error);
        Check(Parent(new DateOnly(1920, 1, 1), ChildBorn, Gender.Female)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(Gender.Male)]
    [InlineData(Gender.Unknown)]
    public void ChildBornAfterAFathersDeath_IsAWarningOnlyAfterAYear(Gender gender)
    {
        Check(Parent(new DateOnly(1920, 1, 1), ChildBorn.AddMonths(-11), gender)).Should().BeEmpty();
        Check(Parent(new DateOnly(1920, 1, 1), ChildBorn.AddMonths(-13), gender))
            .Should().ContainSingle(f => f.Severity == PlausibilitySeverity.Warning);
    }

    [Fact]
    public void MissingDates_GiveNoFindings()
    {
        Check(Parent(null)).Should().BeEmpty();
        PlausibilityRules.CheckParentChild(Parent(new DateOnly(1920, 1, 1)), Child with { BirthDate = null })
            .Should().BeEmpty();
    }

    [Fact]
    public void OverAHundredAndTenWithNoDeathDate_IsAWarning()
    {
        var today = new DateOnly(2026, 10, 25);
        PlausibilityRules.CheckPerson(new LifeDates("Old Test", new DateOnly(1915, 1, 1), null, null), today)
            .Should().ContainSingle(f => f.Severity == PlausibilitySeverity.Warning && f.Message.Contains("111"));
        PlausibilityRules.CheckPerson(new LifeDates("Old Test", new DateOnly(1917, 1, 1), null, null), today)
            .Should().BeEmpty();
    }

    [Fact]
    public void DyingOverAHundredAndTen_IsAWarning()
    {
        var today = new DateOnly(2026, 10, 25);
        PlausibilityRules.CheckPerson(new LifeDates("Old Test", new DateOnly(1800, 1, 1), new DateOnly(1915, 1, 1), null), today)
            .Should().ContainSingle(f => f.Severity == PlausibilitySeverity.Warning);
        PlausibilityRules.CheckPerson(new LifeDates("Old Test", new DateOnly(1800, 1, 1), new DateOnly(1900, 1, 1), null), today)
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("2000-03-10", "2010-03-09", 9)]
    [InlineData("2000-03-10", "2010-03-10", 10)]
    public void AgeOn_CountsWholeYears(string born, string on, int expected) =>
        PlausibilityRules.AgeOn(DateOnly.Parse(born), DateOnly.Parse(on)).Should().Be(expected);
}
