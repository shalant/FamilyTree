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
    public void AfterRegistering_FormIsReplacedBySuccessBlock_SoTheNextStepStaysOnScreen()
    {
        var cut = RenderComponent<Register>();

        var fields = cut.FindAll("input");
        fields[0].Change("Test");
        fields[1].Change("Person");
        fields[2].Change("test.person@example.com");
        fields[3].Change("A-long-enough-pass1");
        fields[4].Change("A-long-enough-pass1");
        cut.Find("button.mud-button-filled").Click();

        cut.Markup.Should().Contain("You're all set!");
        cut.Markup.Should().Contain("Link to your tree");
        cut.FindAll("input[type=text],input[type=email],input[type=password]")
            .Where(i => i.GetAttribute("type") != "hidden")
            .Should().BeEmpty("the sign-up fields must not stay on screen above the next-step button");
        cut.Markup.Should().NotContain("Create your account");
    }
}
