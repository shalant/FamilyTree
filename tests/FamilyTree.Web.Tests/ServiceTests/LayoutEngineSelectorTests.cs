using FamilyTree.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FamilyTree.Web.Tests.ServiceTests;

// LayoutEngineSelector (2026-10-24): the super-user can switch to the Timeline prototype on
// live data; everyone else stays on the site default. Since 2026-10-25 the super-user has
// three: archived, the best Timeline version so far, and the newest Timeline code. The rule that matters most is that a
// family member can never select the prototype, even when their browser holds the
// super-user's saved choice (the localStorage key is per browser, not per account).
public class LayoutEngineSelectorTests
{
    private static LayoutEngineSelector Create(bool superUser, string? siteDefault = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(siteDefault is null ? [] : [new("Layout:Engine", siteDefault)])
            .Build();
        return new LayoutEngineSelector(
            new FamilyTreeLayoutEngine(), new BestTimelineLayoutEngine(), new TimelineLayoutEngine(), config,
            new FakeCurrentUserService { IsSuperUser = superUser });
    }

    [Fact]
    public void FamilyMember_StaysOnLegacy_AndCannotSelectThePrototype()
    {
        var selector = Create(superUser: false);
        var changed = 0;
        selector.Changed += () => changed++;

        selector.Available.Select(o => o.Key).Should().Equal(LayoutEngineSelector.LegacyKey);
        selector.TrySelect("Timeline").Should().BeFalse();
        selector.TrySelect(LayoutEngineSelector.BestKey).Should().BeFalse();

        selector.Current.Key.Should().Be(LayoutEngineSelector.LegacyKey);
        selector.Current.Engine.Should().BeOfType<FamilyTreeLayoutEngine>();
        changed.Should().Be(0);
    }

    [Fact]
    public void SuperUser_CanSwitchToThePrototypeAndBack()
    {
        var selector = Create(superUser: true);
        var changed = 0;
        selector.Changed += () => changed++;

        selector.Current.Key.Should().Be(LayoutEngineSelector.LegacyKey);
        selector.Available.Select(o => o.Key).Should()
            .Equal(LayoutEngineSelector.LegacyKey, LayoutEngineSelector.BestKey, LayoutEngineSelector.TimelineKey);

        selector.TrySelect("timeline").Should().BeTrue(); // case-insensitive, like Layout:Engine
        selector.Current.Engine.Should().BeOfType<TimelineLayoutEngine>();

        selector.TrySelect(LayoutEngineSelector.BestKey).Should().BeTrue();
        selector.Current.Engine.Should().BeOfType<BestTimelineLayoutEngine>();

        selector.TrySelect("Legacy").Should().BeTrue();
        selector.Current.Engine.Should().BeOfType<FamilyTreeLayoutEngine>();
        changed.Should().Be(3);
    }

    [Fact]
    public void SelectingTheCurrentEngineAgain_DoesNotRaiseChanged()
    {
        var selector = Create(superUser: true);
        var changed = 0;
        selector.Changed += () => changed++;

        selector.TrySelect("Legacy").Should().BeTrue();

        changed.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Sugiyama")]
    public void UnknownOrMissingKey_IsRefused(string? key)
    {
        var selector = Create(superUser: true);

        selector.TrySelect(key).Should().BeFalse();
        selector.Current.Key.Should().Be(LayoutEngineSelector.LegacyKey);
    }

    // Layout__Engine=Timeline on the App Service still switches the whole site, as before:
    // the default draws for everyone, and SuperUserOnly only limits who may pick an engine.
    [Fact]
    public void SiteDefaultTimeline_DrawsForFamilyMembersToo()
    {
        var selector = Create(superUser: false, siteDefault: "Timeline");

        selector.Current.Engine.Should().BeOfType<TimelineLayoutEngine>();
        selector.Available.Select(o => o.Key).Should()
            .Equal(LayoutEngineSelector.LegacyKey, LayoutEngineSelector.TimelineKey);
    }

    [Fact]
    public void UnrecognisedSiteDefault_FallsBackToLegacy()
    {
        var selector = Create(superUser: false, siteDefault: "Bogus");

        selector.Current.Key.Should().Be(LayoutEngineSelector.LegacyKey);
    }

    [Fact]
    public void SelectedLayoutEngine_DrawsWithWhicheverEngineIsSelected()
    {
        var selector = Create(superUser: true);
        var engine = new SelectedLayoutEngine(selector);
        var people = new List<FamilyTree.Shared.DTOs.Person.PersonDto>();
        var couples = new List<FamilyTree.Shared.DTOs.CoupleDto>();

        engine.ComputeLayout(people, couples, null).Should()
            .BeEquivalentTo(new FamilyTreeLayoutEngine().ComputeLayout(people, couples, null));

        selector.TrySelect("Timeline");
        engine.ComputeLayout(people, couples, null).Should()
            .BeEquivalentTo(new TimelineLayoutEngine().ComputeLayout(people, couples, null));
    }
}
