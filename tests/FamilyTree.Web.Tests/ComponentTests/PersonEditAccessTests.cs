using Bunit;
using FamilyTree.Core.Services;
using FamilyTree.Web.Modules.Components;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// A Member editing themselves sees no family-link pickers (2026-11-26).
public class PersonEditAccessTests : ComponentTestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LockedRelationships_HideTheLinkPickers(bool locked)
    {
        Services.AddSingleton<IMediumService>(new FakeMediumServiceForPersonForm());
        RenderComponent<MudPopoverProvider>();

        var cut = RenderComponent<PersonForm>(p => p.Add(x => x.LockRelationships, locked));

        cut.FindAll(".ft-relationships-toggle").Should().HaveCount(locked ? 0 : 1);
        cut.Markup.Contains("Family links are managed by an admin.").Should().Be(locked);
    }
}
