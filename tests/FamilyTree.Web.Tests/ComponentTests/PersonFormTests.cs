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
    private readonly IRenderedComponent<MudPopoverProvider> _popovers;

    public PersonFormTests()
    {
        Services.AddSingleton<IMediumService>(new FakeMediumServiceForPersonForm());

        // MudSelect (Gender) and MudDatePicker resolve popovers through the shared
        // PopoverService — it throws "Missing <MudPopoverProvider />" unless one has
        // rendered first (same requirement as CustomAppBarTests' MudAutocomplete/MudMenu).
        _popovers = RenderComponent<MudPopoverProvider>();
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

    // Found on a phone 2026-11-26: the form showed "Upload failed: ... NotReadableError ...".
    [Fact]
    [Trait("Category", "Regression")]
    public async Task UnreadablePhoto_ShowsPlainMessageAndToast_AndTellsTheAdmin()
    {
        var cut = RenderComponent<PersonForm>();
        var input = cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>();

        await cut.InvokeAsync(() => input.Instance.OnChange.InvokeAsync(
            new Microsoft.AspNetCore.Components.Forms.InputFileChangeEventArgs([new UnreadableFile()])));

        cut.Markup.Should().Contain("be opened. Save it to your device first").And.NotContain("NotReadableError");
        Services.GetRequiredService<FamilyTree.Web.Services.ToastService>().Toasts.Should().ContainSingle()
            .Which.Message.Should().StartWith("That photo couldn't be opened");
        UploadFailures.Reports.Should().ContainSingle();
    }

    // Found live 2026-11-27: the parent picker stopped at the 25th name with nothing to say so,
    // and a later person looked as if they weren't on the tree.
    [Theory]
    [Trait("Category", "Regression")]
    [InlineData(25, false)]
    [InlineData(26, true)]
    public async Task RelationshipPicker_SaysThereAreMore_WhenTheListIsCut(int people, bool hint)
    {
        var all = Enumerable.Range(1, people)
            .Select(i => new PersonDto { Id = Guid.NewGuid(), FirstName = $"Person{i:00}", LastName = "Small" })
            .ToList();
        var cut = RenderComponent<PersonForm>(p => p.Add(f => f.AllPeople, all));
        await ClickAsync(cut.FindAll("button").First(b => b.TextContent.Contains("Add relationships") || b.TextContent.Contains("Show")));
        var picker = cut.FindComponents<MudAutocomplete<PersonDto>>()[0];

        await cut.InvokeAsync(() => picker.Instance.OpenMenuAsync());

        _popovers.WaitForAssertion(() => _popovers.Markup.Should().Contain("Person25"));
        _popovers.Markup.Contains("Type to find more").Should().Be(hint);
    }

    private static string GetInitials(IRenderedComponent<PersonForm> cut)
    {
        var property = typeof(PersonForm).GetProperty("Initials",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (string)property.GetValue(cut.Instance)!;
    }

    [Fact]
    [Trait("Category", "Regression")]
    public async Task AvatarInitialsPreview_ShowsBothInitials_WhenNamesAreFilled()
    {
        var cut = RenderComponent<PersonForm>();

        await ChangeAsync(cut.Find("input[placeholder='e.g. Margaret']"), "John");
        await ChangeAsync(cut.Find("input[placeholder='e.g. Rosenberg']"), "Smith");

        var initialsDiv = cut.Find("div[style*='letter-spacing:0.02em']");
        initialsDiv.TextContent.Should().Be("JS");
    }

    // Regression for a silent no-op (docs/TodoList.md, golden-path walkthrough):
    // clicking Submit with blank required fields used to do nothing visible at all —
    // no error, no toast, OnSubmit just never fired. PersonForm.razor's Submit()
    // now sets _firstNameError/_lastNameError explicitly before bailing out.
    [Fact]
    [Trait("Category", "Regression")]
    public async Task Submit_ShowsRequiredErrors_AndDoesNotInvokeOnSubmit_WhenNamesAreBlank()
    {
        var submittedDtos = new List<PersonUpsertDto>();
        var cut = RenderComponent<PersonForm>(parameters => parameters
            .Add(p => p.OnSubmit, EventCallback.Factory.Create<PersonUpsertDto>(this, dto => submittedDtos.Add(dto))));

        var saveButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Save");
        await ClickAsync(saveButton);

        submittedDtos.Should().BeEmpty();
        cut.Markup.Should().Contain("First name is required.");
        cut.Markup.Should().Contain("Last name is required.");
    }

    [Fact]
    [Trait("Category", "Regression")]
    public async Task Submit_InvokesOnSubmit_WhenRequiredFieldsAreFilled()
    {
        var submittedDtos = new List<PersonUpsertDto>();
        var cut = RenderComponent<PersonForm>(parameters => parameters
            .Add(p => p.OnSubmit, EventCallback.Factory.Create<PersonUpsertDto>(this, dto => submittedDtos.Add(dto))));

        await ChangeAsync(cut.Find("input[placeholder='e.g. Margaret']"), "John");
        await ChangeAsync(cut.Find("input[placeholder='e.g. Rosenberg']"), "Smith");

        var saveButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Save");
        await ClickAsync(saveButton);

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
    // Edit mode loads the person's photos after the first render; a person with none is the common case.
    public Task<ServiceResponse<List<MediumDto>>> GetByPersonIdAsync(Guid personId, CancellationToken ct = default)
        => Task.FromResult(ServiceResponse<List<MediumDto>>.Ok([]));
    public Task<ServiceResponse<MediumDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<MediumDto>> CreateAsync(MediumUpsertDto dto, Stream fileStream, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<MediumDto>> UpdateAsync(Guid id, MediumUpsertDto dto, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse> DeleteAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException();
}
