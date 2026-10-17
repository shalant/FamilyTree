using Bunit;
using FamilyTree.Core.Services;
using FamilyTree.Web.Modules.Pages;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

public class RegisterSuccessStateTests : ComponentTestBase
{
    public RegisterSuccessStateTests()
    {
        Services.AddSingleton<IAuthService>(new FakeAuthService { RegisteredUserId = Guid.NewGuid() });
        Services.AddSingleton<IStoryService>(new FakeStoryServiceForRegister());
    }

    // Found live 2026-10-11 during the production smoke test: after "Create account" the
    // success block ("You're all set!" + "Link to your tree") was rendered underneath the
    // entire still-visible form, so on a shorter window the only next step sat below the
    // fold and read as cut off. The form is now replaced by the success block.
    [Fact]
    [Trait("Category", "Regression")]
    public async Task AfterRegistering_FormIsReplacedBySuccessBlock_SoTheNextStepStaysOnScreen()
    {
        var cut = RenderComponent<Register>();

        var fields = cut.FindAll("input");
        await ChangeAsync(fields[0], "Test");
        await ChangeAsync(fields[1], "Person");
        await ChangeAsync(fields[2], "test.person@example.com");
        await ChangeAsync(fields[3], "A-long-enough-pass1");
        await ChangeAsync(fields[4], "A-long-enough-pass1");
        await ClickAsync(cut.Find("button.mud-button-filled"));

        cut.Markup.Should().Contain("You're all set!");
        cut.Markup.Should().Contain("Link to your tree");
        cut.FindAll("input[type=text],input[type=email],input[type=password]")
            .Where(i => i.GetAttribute("type") != "hidden")
            .Should().BeEmpty("the sign-up fields must not stay on screen above the next-step button");
        cut.Markup.Should().NotContain("Create your account");
    }
}
