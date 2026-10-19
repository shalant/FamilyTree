using FamilyTree.Web.Services;

namespace FamilyTree.Web.Tests.ServiceTests;

// Which layout engine the fuzz harness and the transition pairs lay out with (ADR 004,
// step 4). The archived FamilyTreeLayoutEngine unless a test switches it for its own
// execution flow, which TimelineEngineSuiteTests does to rerun both suites, unchanged,
// against TimelineLayoutEngine. AsyncLocal, so tests running in parallel never see each
// other's choice.
internal static class LayoutEngineOverride
{
    private static readonly AsyncLocal<Func<IFamilyTreeLayoutEngine>?> Current = new();

    public static IFamilyTreeLayoutEngine Create() => Current.Value?.Invoke() ?? new FamilyTreeLayoutEngine();

    public static IDisposable Use(Func<IFamilyTreeLayoutEngine> make)
    {
        var previous = Current.Value;
        Current.Value = make;
        return new Restore(() => Current.Value = previous);
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}
