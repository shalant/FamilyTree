using Xunit;

// Collections run in parallel by default; E2eAppFixture binds a hardcoded port
// (44777), so two fixture instances (one per collection using it) racing to bind
// that port at the same time would break both. Only matters now that a second
// collection exists (see E2eIsolatedCollection) — sequential is also just correct
// for E2E tests driving one shared app instance regardless.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FamilyTree.E2E.Tests;

[CollectionDefinition("E2E")]
public class E2eCollection : ICollectionFixture<E2eAppFixture>;

// A dedicated collection (its own E2eAppFixture instance — separate app process,
// separate database) for tests that shouldn't share app/circuit state with the
// main "E2E" collection. Added after CsrfProtectionTests.DoLogout_WithoutAntiforgeryToken_
// DoesNotSignTheUserOut was found to reliably break whichever test ran immediately
// after it in the shared collection (GoldenPathTests timing out waiting on a
// dropdown option) — root cause not fully pinned down (leading theory: something
// about that test's two real Blazor circuits plus a raw forged POST leaves a DB
// connection/transaction in a state that blocks the next test's query), so
// isolating it is the pragmatic fix pending deeper investigation.
[CollectionDefinition("E2E-Isolated")]
public class E2eIsolatedCollection : ICollectionFixture<E2eAppFixture>;
