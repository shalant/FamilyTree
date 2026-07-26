using Bunit;
using FamilyTree.Core.Services;
using FamilyTree.Shared.DTOs.StoryInvite;
using FamilyTree.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

public abstract class ComponentTestBase : TestContext
{
    protected ComponentTestBase()
    {
        // App services
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<FamilyTreeLayoutEngine>();

        // MudBlazor services
        Services.AddMudServices();
        Services.AddSingleton<IDialogService, DialogService>();

        // TimeProvider for MudTextField
        Services.AddSingleton(TimeProvider.System);

        // Mock StoryInviteService
        //Services.AddSingleton<IStoryInviteService, FakeStoryInviteService>();

        // JSInterop modules
        JSInterop.SetupModule("/js/canvas-interaction.js?v=1");

        // Mock all unhandled JSInterop calls
        JSInterop.SetupVoid("focusElement", _ => true);
        JSInterop.SetupVoid("setInputText", _ => true);
        JSInterop.SetupVoid("applyDragStyle", _ => true);
        JSInterop.SetupVoid("mudElementRef.addOnBlurEvent", _ => true);
        JSInterop.SetupVoid("mudElementRef.addOnFocusEvent", _ => true);
        JSInterop.Setup<dynamic>("mudElementRef.getBoundingClientRect", _ => true)
            .SetResult(new { width = 0, height = 0, left = 0, top = 0, bottom = 0, right = 0, x = 0, y = 0 });
    }
}

//// Example fake service
//public class FakeStoryInviteService : IStoryInviteService
//{
//    public Task<StoryInviteValidationDto> ValidateInviteAsync(string token)
//        => Task.FromResult(new StoryInviteValidationDto { IsValid = true });
//}