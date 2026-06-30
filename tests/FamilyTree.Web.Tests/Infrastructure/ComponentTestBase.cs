using Bunit;
using FamilyTree.Core.Services;
using FamilyTree.Shared.DTOs.StoryInvite;
using FamilyTree.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

public abstract class ComponentTestBase : TestContext
{
    protected ComponentTestBase()
    {
        // App services
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<FamilyTreeLayoutEngine>();

        // MudBlazor services
        Services.AddSingleton<IDialogService, DialogService>();

        // Mock StoryInviteService
        //Services.AddSingleton<IStoryInviteService, FakeStoryInviteService>();

        // JSInterop modules
        JSInterop.SetupModule("/js/canvas-interaction.js");
    }
}

//// Example fake service
//public class FakeStoryInviteService : IStoryInviteService
//{
//    public Task<StoryInviteValidationDto> ValidateInviteAsync(string token)
//        => Task.FromResult(new StoryInviteValidationDto { IsValid = true });
//}