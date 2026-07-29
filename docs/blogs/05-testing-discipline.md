# Testing Discipline: Constraints Over Code Review

**Posted:** July 29, 2026  
**Category:** Quality & Testing Strategy

## The Problem We Solved

ArborKin's family tree rendering broke mid-session when a user restored their mother (Dora) to the tree. Suddenly, a completely unrelated person (Marc, Bud's son) had a connector stretching across the entire canvas toward the wrong branch.

What went wrong? The layout engine had a bug: it detected cross-family couples (people from two different family branches who married) correctly *until* one branch was nested under a restored grandparent. Once the branch depth changed, the detection code silently stopped working.

**This bug was not caught by code review.** It was caught by **a regression test** written after a *previous* bug in the same area.

## Why Code Review Fails for Complex Logic

Code review relies on a human reading code and spotting mistakes. For layout algorithms, this is surprisingly hard:

- "Does this code correctly detect a couple when the person is nested 3 levels deep?"
- "Will this still work if the parent-child relationships are added in reverse order?"
- "Does this handle the case where one spouse is in a different family?"

These questions require you to mentally trace through nested loops, conditional branches, and data structure transformations. Smart people will still miss edge cases.

## Testing Discipline: Constraints That Catch Bugs

Instead of relying on code review, ArborKin uses **comprehensive tests as constraints**:

```csharp
[Fact]
public void CrossRootCouple_StillDetectedWhenOneSideIsNestedUnderAGrandparent()
{
    // Setup: Build a scenario where Marc (son of Bud+Florence) 
    // marries Ellen (daughter of Ray+Rose). Bud's family is
    // nested under Dora (his restored mother).
    var dora = new Person { Id = guid1, FirstName = "Dora" };
    var bud = new Person { Id = guid2, FirstName = "Bud", ParentIds = [dora.Id] };
    var florence = new Person { Id = guid3, FirstName = "Florence" };
    var marc = new Person { Id = guid4, FirstName = "Marc", ParentIds = [bud.Id, florence.Id] };
    
    var ray = new Person { Id = guid5, FirstName = "Ray" };
    var rose = new Person { Id = guid6, FirstName = "Rose" };
    var ellen = new Person { Id = guid7, FirstName = "Ellen", ParentIds = [ray.Id, rose.Id] };
    
    var people = [dora, bud, florence, marc, ray, rose, ellen];
    var relationships = [
        // Parents/children...
        new Relationship { PersonA = bud, PersonB = dora, Type = Parent },
        // ...
        new Relationship { PersonA = marc, PersonB = ellen, Type = Spouse }
    ];
    
    // Act
    var layout = layoutEngine.ComputeLayout(people, relationships);
    
    // Assert: Marc and Ellen should have the same X position (couple at midpoint)
    // or at minimum, should NOT have their connector stretching across the canvas
    var marcPos = layout.NodePositions[marc.Id];
    var ellenPos = layout.NodePositions[ellen.Id];
    Math.Abs(marcPos.X - ellenPos.X).Should().BeLessThan(50, "couple should be positioned together");
}
```

This test is:
- **Self-documenting** — the test name is the bug description
- **Reproducible** — runs the same way every time
- **Non-fragile** — doesn't depend on log output or browser timing
- **Regression-safe** — if the bug comes back, the test fails immediately

## The Pattern: Regression Tests as Documentation

When we find a bug:

1. **Write a test that reproduces it** — before fixing the code
2. **Verify the test fails** — confirm it catches the bug
3. **Fix the code**
4. **Verify the test passes** — confirm the fix works
5. **Leave the test in place** — future changes that break this will fail the test

This is expensive (4 commits instead of 1). But it's the only way to prevent the same class of bug from recurring six months later when a new developer refactors the same area.

## Test Coverage Numbers

ArborKin has **124 passing tests**:
- 75 Core tests (services, layout engine, data integrity)
- 49 Web tests (components, Blazor)
- All run automatically in CI on every push

These are not "vanity metrics" (high coverage number, low confidence). They're **specific**:
- `PersonServiceTests` (8 tests): cover create, audit stamping, family scoping, soft delete/restore
- `FamilyTreeLayoutEngineTests` (18 tests): regression suite, one test per discovered bug class
- `RelationshipServiceFamilyScopingTests` (8 tests): specifically for the cross-family read/write gap

## What We Learned

### Uncle Bob Was Right

> "The only way to go fast is to go well. And the only way to go well is to have good tests."

ArborKin's layout engine has 6-7 interacting systems (group placement, cross-root couple detection, connector routing, etc.). Without tests, it would be unmaintainable.

### Tests Are Cheaper Than Debugging

The bug we found (cross-root couple detection breaking when nested) took:
- 2 hours to debug in production
- 1 hour to write the test
- 30 min to fix the code

A code review would not have caught this. But the regression test will prevent it forever.

### Constraints > Code Review

Instead of hoping a reviewer catches bugs, build tests that *guarantee* certain invariants hold:

- "Couples are always positioned together" ✓ (test)
- "Family scoping never leaks cross-family data" ✓ (test)
- "Soft-deleted people are never returned by normal queries" ✓ (test)

These are now machine-checkable facts, not hope.

## Next: Acceptance Tests

We have unit tests. Next step: **acceptance tests** (Gherkin/BDD) for user journeys:

```gherkin
Scenario: Register as invited family member
  Given I have an invite token for the Smith family
  When I complete the signup form with "John Smith"
  And I link myself to "Bill Smith" as his son
  Then my profile is created and linked
  And I see the family tree with my focus person as "John"
  And no duplicate "John Smith" record was created
```

These bridge the gap between business requirements and unit tests.

---

**Philosophy:** [Constraints over subjectivity](https://www.youtube.com/watch?v=Qw0ip6Y0Gks) — let tests decide what's correct, not code reviewers.
