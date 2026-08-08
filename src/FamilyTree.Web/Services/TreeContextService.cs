using FamilyTree.Shared.DTOs.Person;

namespace FamilyTree.Web.Services;

public sealed class TreeContextService
{
    public PersonDto? FocusPerson { get; private set; }
    public int PeopleCount { get; private set; }
    public int CoupleCount { get; private set; }
    public bool MobilePanelOpen { get; private set; }

    public event Action? OnChange;
    public event Action? OnCenterTreeRequested;
    public event Action? OnZoomInRequested;
    public event Action? OnZoomOutRequested;
    public event Action? OnResetViewRequested;

    public void SetContext(PersonDto? focusPerson, int peopleCount, int coupleCount)
    {
        FocusPerson = focusPerson;
        PeopleCount = peopleCount;
        CoupleCount = coupleCount;
        OnChange?.Invoke();
    }

    public void RequestCenterTree() => OnCenterTreeRequested?.Invoke();
    public void RequestZoomIn() => OnZoomInRequested?.Invoke();
    public void RequestZoomOut() => OnZoomOutRequested?.Invoke();
    public void RequestResetView() => OnResetViewRequested?.Invoke();

    // Toggled from either the AppBar identity tap or the canvas mini-trigger
    // (CustomToolbar's collapsed pill, on mobile) — both entry points share
    // this one piece of state rather than each owning their own.
    public void ToggleMobilePanel()
    {
        MobilePanelOpen = !MobilePanelOpen;
        OnChange?.Invoke();
    }

    public void CloseMobilePanel()
    {
        if (!MobilePanelOpen) return;
        MobilePanelOpen = false;
        OnChange?.Invoke();
    }
}
