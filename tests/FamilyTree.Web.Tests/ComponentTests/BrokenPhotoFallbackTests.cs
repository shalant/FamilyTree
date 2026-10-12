using Bunit;
using FamilyTree.Core.Services;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Modules.Components;
using FamilyTree.Web.Services;
using FluentAssertions;
using ErrorEventArgs = Microsoft.AspNetCore.Components.Web.ErrorEventArgs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// Found on a real phone (2026-10-12): a profile photo whose URL no longer loads showed the
// browser's broken-image alt text squeezed inside the avatar circle. Both places that draw
// a profile photo now fall back to the initials when the image fires its error event.
public class BrokenPhotoFallbackTests : ComponentTestBase
{
    private static PersonDto WithPhoto() => new()
    {
        Id = Guid.NewGuid(), FirstName = "Willa", LastName = "Small",
        ProfilePhotoUrl = "https://example.invalid/missing.jpg",
    };

    [Fact]
    [Trait("Category", "Regression")]
    public void CanvasNode_FallsBackToInitials_WhenPhotoFailsToLoad()
    {
        var cut = RenderComponent<PersonNode>(p => p
            .Add(x => x.Person, WithPhoto())
            .Add(x => x.Left, 0).Add(x => x.Top, 0).Add(x => x.Size, 60)
            .Add(x => x.RelativeDepth, 0).Add(x => x.IsFocus, false));

        cut.FindAll("img").Should().HaveCount(1);
        cut.Find("img").TriggerEvent("onerror", new ErrorEventArgs());

        cut.FindAll("img").Should().BeEmpty();
        cut.Markup.Should().Contain("WS");
    }

    [Fact]
    [Trait("Category", "Regression")]
    public void DetailDrawer_ShowsInitialsAndANote_WhenPhotoFailsToLoad()
    {
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<FamilyTreeLayoutEngine>();
        Services.AddSingleton<IMediumService>(new FakeMediumService());
        Services.AddSingleton<IStoryService>(new FakeStoryService());

        var cut = RenderComponent<PersonDetailDrawer>(p => p.Add(x => x.Person, WithPhoto()));

        cut.Markup.Should().NotContain("Photo couldn't be loaded");
        cut.Find("img.ft-photo").TriggerEvent("onerror", new ErrorEventArgs());

        cut.FindAll("img.ft-photo").Should().BeEmpty();
        cut.Markup.Should().Contain("Photo couldn't be loaded");
        cut.FindAll(".ft-photo-broken-badge").Should().HaveCount(1);
    }
}
