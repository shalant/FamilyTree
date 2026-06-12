using FamilyTree.Shared.DTOs.Person;

namespace FamilyTree.Web.Services;

public sealed class TreeContextService
{
    public PersonDto? FocusPerson { get; private set; }
    public int PeopleCount { get; private set; }
    public int CoupleCount { get; private set; }

    public event Action? OnChange;
    public event Action? OnCenterTreeRequested;

    public void SetContext(PersonDto? focusPerson, int peopleCount, int coupleCount)
    {
        FocusPerson = focusPerson;
        PeopleCount = peopleCount;
        CoupleCount = coupleCount;
        OnChange?.Invoke();
    }

    public void RequestCenterTree() => OnCenterTreeRequested?.Invoke();
}
