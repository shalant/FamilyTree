using Bunit;
using FamilyTree.Core.Services;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Story;
using FamilyTree.Web.Modules.Pages;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// UX/UI review #8 (docs/uxui/, 2026-11-17): with no stories the page showed two identical
// "Add story" buttons (the header's and the empty card's) inside a frosted-glass card.
// The empty card now holds the page's one action, in the same card style as the stories.
public class StoriesPageTests : ComponentTestBase
{
    private readonly FakeStoryService _stories = new();

    public StoriesPageTests()
    {
        Services.AddSingleton<IPersonService>(new FakePersonService());
        Services.AddSingleton<IStoryService>(_stories);
        RenderComponent<MudPopoverProvider>();
    }

    [Fact]
    public void NoStories_OffersOneAddAction_InsideTheEmptyCard()
    {
        var cut = RenderComponent<Stories>();

        cut.Markup.Should().Contain("No stories yet");
        var buttons = cut.FindAll("button").Where(b => b.TextContent.Contains("story", StringComparison.OrdinalIgnoreCase)).ToList();
        buttons.Should().ContainSingle("the header button is hidden while the empty card offers the action");
        cut.Find(".ft-stories-empty").QuerySelector("button").Should().NotBeNull();
        cut.Markup.Should().NotContain("backdrop-filter");
    }

    [Fact]
    public void WithStories_ShowsTheHeaderAddButton_AndNoEmptyCard()
    {
        _stories.Approved.Add(new StoryDto { Id = Guid.NewGuid(), Title = "Arriving Too Early", Body = "One day", CreatedAt = DateTime.UtcNow });

        var cut = RenderComponent<Stories>();

        cut.FindAll(".ft-stories-empty").Should().BeEmpty();
        cut.FindAll("button").Should().ContainSingle(b => b.TextContent.Contains("Add story"));
    }

    private sealed class FakeStoryService : IStoryService
    {
        public List<StoryDto> Approved { get; } = [];

        public Task<ServiceResponse<List<StoryDto>>> GetAllApprovedAsync(CancellationToken ct = default)
            => Task.FromResult(ServiceResponse<List<StoryDto>>.Ok(Approved));

        public Task<ServiceResponse<List<StoryDto>>> GetByPersonAsync(Guid personId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<List<StoryDto>>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<StoryDto>> CreateAsync(StoryUpsertDto dto, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<StoryDto>> UpdateAsync(Guid id, StoryUpsertDto dto, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse> DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<List<StoryDto>>> GetUnlinkedAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<List<StoryDto>>> GetByAuthorEmailAsync(string email, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<StoryDto?>> GetPendingLinkingSubjectAsync(string email, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<StoryDto>> LinkToPersonAsync(Guid storyId, Guid personId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<StoryDto>> ApproveAsync(Guid storyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<StoryDto>> SetHiddenAsync(Guid storyId, bool hidden, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse> MoveAsync(Guid storyId, int direction, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
