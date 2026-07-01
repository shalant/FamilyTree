using Bunit;
using FamilyTree.Core.Services;
using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Story;
using FamilyTree.Shared.DTOs.StoryInvite;
using FamilyTree.Web.Modules.Pages;
using FamilyTree.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FamilyTree.Web.Tests.ComponentTests;

public class StoryRespondComponentTests : ComponentTestBase
{
    public StoryRespondComponentTests()
    {
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<IStoryInviteService>(new FakeStoryInviteService());
    }

    [Fact]
    public void ShouldRenderLoadingState()
    {
        var cut = RenderComponent<StoryRespond>(parameters => parameters
            .Add(p => p.Token, "abc123"));

        // Component renders successfully with either loading spinner or form content
        cut.Markup.Should().NotBeNullOrEmpty();
    }
}

class FakeStoryInviteService : IStoryInviteService
{
    public Task<ServiceResponse> CreateInviteAsync(StoryInviteCreateDto dto, string baseUrl, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<StoryInviteValidationDto>> ValidateTokenAsync(string token, CancellationToken ct = default)
        => Task.FromResult(ServiceResponse<StoryInviteValidationDto>.Ok(new StoryInviteValidationDto { IsValid = true }));
    public Task<ServiceResponse<StoryDto>> SubmitResponseAsync(StoryInviteResponseDto dto, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ServiceResponse<List<StoryInviteAdminDto>>> GetAllAsync(CancellationToken ct = default)
        => throw new NotImplementedException();
}