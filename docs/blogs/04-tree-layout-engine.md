# Tree Layout Without JavaScript: Computing Positions in C#

**Posted:** July 29, 2026  
**Category:** Architecture & Performance

## The Question: Where Should Layout Happen?

When you build a family tree visualization, you need to place nodes at specific (X, Y) coordinates on a canvas. Traditional approaches:

1. **Browser-side** — render nodes, measure their DOM positions, calculate layout in JavaScript
2. **Server-side** — compute all positions, send coordinates to the browser, render static positions

ArborKin chose approach #2: **compute positions in C# before rendering.**

Why? **Determinism.** Server-side layout is reproducible, testable, and not prone to browser/rendering quirks.

## The C# Layout Engine

`FamilyTreeLayoutEngine` is a pure function: given people and relationships, it returns (X, Y) coordinates for every node.

```csharp
public class LayoutResult
{
    public Dictionary<Guid, (double X, double Y)> NodePositions { get; set; }
    public List<ConnectorPath> Connectors { get; set; }
}

public LayoutResult ComputeLayout(List<PersonDto> people, List<RelationshipDto> relationships)
{
    // 1. Build nuclear family groups (couples + children)
    var couples = BuildNuclearGroups(people, relationships);
    
    // 2. Arrange groups hierarchically (generation-based)
    var positions = PlaceGroups(couples, ...);
    
    // 3. Compute SVG connector paths using the same coordinates
    var connectors = DrawConnectors(positions, relationships);
    
    return new LayoutResult { NodePositions = positions, Connectors = connectors };
}
```

## Key Design Decisions

### Y-Axis is a Timeline

The Y position is based on **birth year**, not generation depth:
```csharp
const double PxPerYear = 6.5;  // 6.5 pixels per birth year
var birthYear = person.BirthDate.Year;
var yPosition = (birthYear - baseYear) * PxPerYear;
```

This gives genealogically accurate vertical positioning — a person born in 1950 is always further down than one born in 1940, regardless of generation.

### Node Sizes Vary by Importance

```csharp
const double FocusSize = 80;      // User's focus person
const double Gen1Size = 70;       // Grandparents/oldest generation
const double DefaultSize = 60;    // Everyone else
```

### Canonical Ordering for Stability

Relationships are stored with `PersonAId < PersonBId` (lexicographically):
```csharp
var (a, b) = personAId < personBId 
    ? (personAId, personBId) 
    : (personBId, personAId);
```

This ensures couples don't jump around the canvas when rendering order changes.

## Regression Testing in Practice

We discovered a subtle bug: once a single-parent root person gained a spouse, their entire nuclear group would jump to the wrong side of the tree.

Root cause: couples were sorted by dictionary insertion order (which person's child was encountered first alphabetically) instead of their own identity.

**The test that caught it:**
```csharp
[Fact]
public void MarryingAnOrphanSiblingRoot_DoesNotJumpTheirGroupPastUnrelatedRootsByAlphabeticalAccident()
{
    // Setup: Bill (root individual) and Gish (unrelated person)
    var bill = new PersonDto { Id = guid1, FirstName = "Bill", ... };
    var gish = new PersonDto { Id = guid2, FirstName = "Gish", ... };
    
    // Act: Marry Bill to Gish (Bill transitions from individual to couple)
    var result1 = layoutEngine.ComputeLayout(people: [bill, gish], relationships: []);
    var result2 = layoutEngine.ComputeLayout(people: [bill, gish], relationships: [
        new RelationshipDto { PersonAId = guid1, PersonBId = guid2, Type = Spouse }
    ]);
    
    // Assert: Bill's group shouldn't jump past Gish
    var billX1 = result1[bill.Id].X;
    var billX2 = result2[bill.Id].X;
    billX1.Should().BeLessThan(billX2 + 200);  // Reasonable threshold
}
```

This test started failing mid-session, revealing the insertion-order bug. After fixing (sorting by couple identity instead), it passed consistently.

## What We Didn't Do: Optimization

We have **no special handling** for:
- Collision avoidance between nodes
- Minimizing connector crossings
- Balancing tree width/height

These are real problems in professional genealogy software (like FamilySearch), but for a family tree of 100-200 people, brute-force positioning works fine. Premature optimization is the root of all evil.

## Lessons for Layout Engines

1. **Determinism beats cleverness** — a predictable layout that users can understand beats an "optimal" layout that changes based on rendering order.
2. **Tests are executable specifications** — once you write a test like "marrying a root shouldn't jump them past an unrelated person," you've documented the invariant forever.
3. **Separation of concerns** — layout logic lives in Core (no Blazor dependencies); rendering lives in Web. Easy to test, easy to reuse.
4. **Timeline-based Y-axis is powerful** — it grounds the layout in genealogical reality. A 60-year-old ancestor is always above a 30-year-old descendant, which matches user intuition.

## Next: Mobile Responsiveness for Layout

Current limitation: the canvas is infinite-sized and pans/zooms. On mobile, large trees are hard to navigate. Future work: compute multiple layout variants (compact, outline) and let users switch.

---

**Reference:** `src/FamilyTree.Core/Services/FamilyTreeLayoutEngine.cs`
