using Bunit;
using FluentAssertions;
using Xunit;
using FamilyTree.Web.Modules.Pages;

namespace FamilyTree.Web.Tests.ComponentTests;

public class StoryRespondComponentTests : ComponentTestBase
{
    [Fact]
    public void ShouldRenderLoadingState()
    {
        var cut = RenderComponent<StoryRespond>(parameters => parameters
            .Add(p => p.Token, "abc123"));

        cut.Markup.Should().Contain("MudProgressCircular");
    }
}