using Bunit;
using FamilyTree.Core.Services;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Story;
using FamilyTree.Web.Modules.Components;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

// UX/UI review A2 (docs/uxui/, 2026-11-15): the Admin Stories table showed a red trash icon on
// every row, next to the approve check. Delete now lives in the Edit story dialog, behind the
// same confirmation, so one stray tap on a row can't reach it.
public class AdminStoryEditDialogTests : ComponentTestBase
{
    private static readonly Guid StoryId = Guid.NewGuid();
    private readonly RecordingStoryService _stories = new();

    public AdminStoryEditDialogTests()
    {
        Services.AddSingleton<IStoryService>(_stories);
    }

    private async Task<(IRenderedComponent<MudDialogProvider> Provider, Task<DialogResult?> Result)> OpenAsync()
    {
        var provider = RenderComponent<MudDialogProvider>();
        RenderComponent<MudPopoverProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<AdminStoryEditDialog>
        {
            { d => d.StoryId, StoryId },
            { d => d.InitialTitle, "A title" },
            { d => d.InitialBody, "A memory" },
        };
        var reference = await provider.InvokeAsync(() => dialogs.ShowAsync<AdminStoryEditDialog>("Edit story", parameters));
        return (provider, reference.Result);
    }

    [Fact]
    public async Task EditDialog_OffersDelete_BehindAConfirmation()
    {
        var (provider, result) = await OpenAsync();

        // Not ClickAsync: that handler waits on the confirmation dialog, which this test answers next.
        provider.FindAll("button").First(b => b.TextContent.Contains("Delete story")).Click();

        // Nothing is deleted until the confirmation is answered.
        _stories.Deleted.Should().BeEmpty();
        provider.FindAll("button").Should().Contain(b => b.TextContent.Trim() == "Delete");

        await ClickAsync(provider.FindAll("button").First(b => b.TextContent.Trim() == "Delete"));

        _stories.Deleted.Should().ContainSingle().Which.Should().Be(StoryId);
        var closed = await result;
        closed!.Canceled.Should().BeFalse();
        closed.Data.Should().Be(StoryId);
    }

    [Fact]
    public async Task EditDialog_CancellingTheConfirmation_DeletesNothing()
    {
        var (provider, _) = await OpenAsync();

        // Not ClickAsync: that handler waits on the confirmation dialog, which this test answers next.
        provider.FindAll("button").First(b => b.TextContent.Contains("Delete story")).Click();
        // The confirmation's own Cancel (the last Cancel button on screen).
        await ClickAsync(provider.FindAll("button").Last(b => b.TextContent.Trim() == "Cancel"));

        _stories.Deleted.Should().BeEmpty();
    }

    private sealed class RecordingStoryService : IStoryService
    {
        public List<Guid> Deleted { get; } = [];

        public Task<ServiceResponse> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            Deleted.Add(id);
            return Task.FromResult(ServiceResponse.Ok());
        }

        public Task<ServiceResponse<List<StoryDto>>> GetByPersonAsync(Guid personId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<List<StoryDto>>> GetAllApprovedAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<List<StoryDto>>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<StoryDto>> CreateAsync(StoryUpsertDto dto, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<StoryDto>> UpdateAsync(Guid id, StoryUpsertDto dto, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<List<StoryDto>>> GetUnlinkedAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<List<StoryDto>>> GetByAuthorEmailAsync(string email, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<StoryDto?>> GetPendingLinkingSubjectAsync(string email, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<StoryDto>> LinkToPersonAsync(Guid storyId, Guid personId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<StoryDto>> ApproveAsync(Guid storyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse<StoryDto>> SetHiddenAsync(Guid storyId, bool hidden, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ServiceResponse> MoveAsync(Guid storyId, int direction, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
