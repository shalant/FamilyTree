using FamilyTree.Core.Services;
using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Person;

namespace FamilyTree.Web.Services;

/// <summary>One layout engine a viewer can pick. <c>SuperUserOnly</c> limits who may pick it,
/// not who sees it: an engine set as the site default (<c>Layout:Engine</c>) draws for everyone.</summary>
public sealed record LayoutEngineOption(string Key, string Name, IFamilyTreeLayoutEngine Engine, bool SuperUserOnly);

/// <summary>
/// Which layout engine draws the tree for the current viewer (scoped: one per circuit).
/// Everyone starts on the site default from <c>Layout:Engine</c> (<c>Legacy</c> unless set);
/// a viewer with more than one option available can switch for themselves. Since 2026-10-24
/// the Timeline prototype is available to the super-user only, so Doug can try it on live data
/// while the family keeps the archived engine (ADR 004). Opening an engine to everyone is a
/// matter of clearing its <c>SuperUserOnly</c> flag.
/// </summary>
public sealed class LayoutEngineSelector
{
    public const string LegacyKey = "Legacy";
    public const string TimelineKey = "Timeline";

    private readonly ICurrentUserService _currentUser;

    public LayoutEngineSelector(
        FamilyTreeLayoutEngine legacy,
        TimelineLayoutEngine timeline,
        IConfiguration configuration,
        ICurrentUserService currentUser)
    {
        _currentUser = currentUser;
        All =
        [
            new(LegacyKey, "Current layout", legacy, SuperUserOnly: false),
            new(TimelineKey, "Timeline prototype", timeline, SuperUserOnly: true),
        ];
        Default = Find(configuration["Layout:Engine"]) ?? All[0];
        Current = Default;
    }

    public IReadOnlyList<LayoutEngineOption> All { get; }
    public LayoutEngineOption Default { get; }
    public LayoutEngineOption Current { get; private set; }

    /// <summary>The engines this viewer may pick: the site default, plus any open to them.</summary>
    public IReadOnlyList<LayoutEngineOption> Available =>
        All.Where(o => o == Default || !o.SuperUserOnly || _currentUser.IsSuperUser).ToList();

    public event Action? Changed;

    /// <summary>Switches to the engine with this key. Returns false, and changes nothing, for an
    /// unknown key or one this viewer may not pick (e.g. a family member's browser holding the
    /// super-user's saved choice).</summary>
    public bool TrySelect(string? key)
    {
        var option = Find(key);
        if (option is null || !Available.Contains(option))
            return false;
        if (option != Current)
        {
            Current = option;
            Changed?.Invoke();
        }
        return true;
    }

    private LayoutEngineOption? Find(string? key) =>
        All.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The <see cref="IFamilyTreeLayoutEngine"/> the canvas and SVG export get: forwards
/// to whichever engine the viewer's <see cref="LayoutEngineSelector"/> has selected.</summary>
public sealed class SelectedLayoutEngine(LayoutEngineSelector selector) : IFamilyTreeLayoutEngine
{
    public FamilyTreeLayout ComputeLayout(
        List<PersonDto> people,
        List<CoupleDto> couples,
        Guid? focusPersonId,
        Guid? anchorPersonId = null) =>
        selector.Current.Engine.ComputeLayout(people, couples, focusPersonId, anchorPersonId);
}
