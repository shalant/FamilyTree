using Bunit;
using FamilyTree.Core.Services;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Medium;
using FamilyTree.Shared.DTOs.Person;
using FamilyTree.Web.Modules.Components;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

public class PersonFormTests : ComponentTestBase
{
    public PersonFormTests()
    {
        Services.AddSingleton<IMediumService>(new FakeMediumServiceForPersonForm());

        // MudSelect (Gender) and MudDatePicker resolve popovers through the shared
        // PopoverService — it throws "Missing <MudPopoverProvider />" unless one has
        // rendered first (same requirement as CustomAppBarTests' MudAutocomplete/MudMenu).
        RenderComponent<MudPopoverProvider>();
    }

    // Regression for the "tofu glyph" bug (docs/TodoList.md, golden-path walkthrough):
    // Initials used to be computed with FirstOrDefault(), which returns '\0' for an
    // empty string — visible in a real browser as a replacement-character glyph in the
    // avatar preview while a name field is still untouched. PersonForm.razor:833's
    // Initials property now guards against empty segments explicitly.
    //
    // Asserted via reflection on the private Initials property, not the rendered DOM —
    // confirmed experimentally that AngleSharp (bUnit's HTML parser) silently drops raw
    // NUL characters during parsing regardless of which code path produced them, so a
    // DOM-text-content assertion here would pass identically whether the underlying bug
    // was present or fixed. Reflection is the only way this test can actually tell the
    // two apart; it was verified to fail against the pre-fix FirstOrDefault() logic and
    // pass against the fix before being left in this form.
    [Fact]
    [Trait("Category", "Regression")]
    public void AvatarInitialsPreview_IsEmpty_NotTofuGlyph_WhenNamesAreBlank()
    {
        var cut = RenderComponent<PersonForm>();

        GetInitials(cut).Should().BeEmpty();
    }

    private static string GetInitials(IRenderedComponent<PersonForm> cut)
    {
        var property = typeof(PersonForm).GetProperty("Initials",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (string)property.GetValue(cut.Instance)!;
    }

    [Fact]
    [Trait("Category", "Regression")]
    public void AvatarInitialsPreview_ShowsBothInitials_WhenNamesAreFilled()
    {
        var cut = RenderComponent<PersonForm>();

        cut.Find("input[placeholder='e.g. Margaret']").Change("John");
        cut.Find("input[placeholder='e.g. Rosenberg']").Change("Smith");

        var initialsDiv = cut.Find("div[style*='letter-spacing:0.02em']");
        initialsDiv.TextContent.Should().Be("JS");
    }

    // Regression for a silent no-op (docs/TodoList.md, golden-path walkthrough):
    // clicking Submit with blank required fields used to do nothing visible at all —
    // no error, no toast, OnSubmit just never fired. PersonForm.razor's Submit()
    // now sets _firstNameError/_lastNameError explicitly before bailing out.
    [Fact]
    [Trait("Category", "Regression")]
    public void Submit_ShowsRequiredErrors_AndDoesNotInvokeOnSubmit_WhenNamesAreBlank()
    {
        var submittedDtos = new List<PersonUpsertDto>();
        var cut = RenderComponent<PersonForm>(parameters => parameters
            .Add(p => p.OnSubmit, EventCallback.Factory.Create<PersonUpsertDto>(this, dto => submittedDtos.Add(dto))));

        var saveButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Save");
        saveButton.Click();

        submittedDtos.Should().BeEmpty();
        cut.Markup.Should().Contain("First name is required.");
        cut.Markup.Should().Contain("Last name is required.");
    }

    [Fact]
    [Trait("Category", "Regression")]
    public void Submit_InvokesOnSubmit_WhenRequiredFieldsAreFilled()
    {
        var submittedDtos = new List<PersonUpsertDto>();
        var cut = RenderComponent<PersonForm>(parameters => parameters
            .Add(p => p.OnSubmit, EventCallback.Factory.Create<PersonUpsertDto>(this, dto => submittedDtos.Add(dto))));

        cut.Find("input[placeholder='e.g. Margaret']").Change("John");
        cut.Find("input[placeholder='e.g. Rosenberg']").Change("Smith");

        var saveButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Save");
        saveButton.Click();

        submittedDtos.Should().ContainSingle();
        submittedDtos[0].FirstName.Should().Be("John");
        submittedDtos[0].LastName.Should().Be("Smith");
    }
}

// Only members PersonForm's Add-mode path can reach are stubbed with real behavior;
// everything else throws so an unexpected call (e.g. edit-mode-only photo loading)
// fails loudly rather than silently returning a default.
class FakeMediumServiceForPersonForm : IMediumService
{
    public Task<ServiceResponse<List<MediumDto>>> GetAllAsync(CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<List<MediumDto>>> GetByPersonIdAsync(Guid personId, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<MediumDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<MediumDto>> CreateAsync(MediumUpsertDto dto, Stream fileStream, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<MediumDto>> UpdateAsync(Guid id, MediumUpsertDto dto, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse> DeleteAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException();
}
