# Data Integrity the Hard Way: Catching Orphaned Relationships Before They Break Production

**Posted:** July 29, 2026  
**Category:** Data Quality & Testing

## The Incident: A Relationship That Shouldn't Exist

While manually testing "restore a deleted person and verify their relationships come back," we found something unexpected:

**Marc Rosenberg's sibling relationship to Elliot appeared *twice*:**
- One live relationship pointing to Elliot (current, live person)
- One live relationship pointing to an *older* Elliot (same name, soft-deleted person)

How did a relationship survive deletion when the person it pointed to was soft-deleted? Our cascade logic should have caught this.

```sql
-- What we found:
SELECT PersonAId, PersonBId, Type, DeletedAt FROM Relationships WHERE PersonAId = 'marc-guid'
-- Output:
-- elliot-v1-guid | marc-guid | Sibling | NULL (live)
-- elliot-v2-guid | marc-guid | Sibling | NULL (live but orphaned!)
```

## Root Cause Analysis

Reconstructing the timeline from audit logs, we found a smoking gun:

1. **June 7** — Original Elliot created during a bulk import
2. **June 24** — Bulk import rollback initiated (user changed their mind)
3. **During rollback** — Elliot's Person row deleted, but...
4. **The orphaned relationship lived on** — Elliot's three original relationships (Sibling→Marc, Parent from Bud, Parent from Florence) still had `DeletedAt = NULL`

**The bug:** `ClaudeImportService.RollbackBatchAsync` soft-deleted every Person in the batch, but only cascade-soft-deleted a Relationship when **both endpoints** were in the batch being rolled back.

```csharp
// Old buggy code (pseudocode):
foreach (var relationship in batch.Relationships)
{
    if (batchPersonIds.Contains(relationship.PersonAId) &&
        batchPersonIds.Contains(relationship.PersonBId))
    {
        // Only delete if BOTH are in the batch
        relationship.DeletedAt = DateTime.UtcNow;
    }
}
```

A relationship linking an imported person to someone who *already existed* (Marc was created in the same batch but was never deleted, only restored) **survived as live but orphaned**.

Not knowing the original Elliot still existed, we created a second "Elliot Rosenberg" person on July 7 with the same relationships. Now Marc had two conflicting sibling relationships.

## The Fix: Three Layers of Defense

### Layer 1: Fix the Rollback Logic (Immediate)

```csharp
// New logic: cascade on EITHER side
foreach (var relationship in batch.Relationships)
{
    if (batchPersonIds.Contains(relationship.PersonAId) ||
        batchPersonIds.Contains(relationship.PersonBId))
    {
        // Delete if EITHER endpoint is in the batch
        relationship.DeletedAt = DateTime.UtcNow;
    }
}
```

Rewritten off `ExecuteUpdateAsync` (which we were using for performance) to tracked-entity updates, so it gets full test coverage with xUnit's InMemory EF provider.

**Tests added:**
```csharp
[Fact]
public void RollbackAsync_RelationshipWithOneEndpointInBatch_SoftDeletesRelationship()
{
    // Setup: imported person A, imported person B, pre-existing person C
    // Relationship: A→C (spans batch and non-batch)
    
    // Act: rollback batch
    
    // Assert: A→C relationship is soft-deleted
}
```

### Layer 2: Automatic Detection on Startup

Added `IDataIntegrityService.FixOrphanedRelationshipsAsync()`:

```csharp
public async Task<ServiceResponse<List<OrphanedRelationshipFixDto>>> FixOrphanedRelationshipsAsync()
{
    var orphaned = await ctx.Relationships
        .Include(r => r.PersonA)
        .Include(r => r.PersonB)
        .Where(r => r.DeletedAt == null &&
                    (r.PersonA.DeletedAt != null || r.PersonB.DeletedAt != null))
        .ToListAsync();

    foreach (var relationship in orphaned)
    {
        relationship.DeletedAt = DateTime.UtcNow;
        relationship.DeletedBy = systemUserId;
    }

    await ctx.SaveChangesAsync();
    // Audit log entry + email alert
    return ServiceResponse.Ok(orphaned);
}
```

This runs on every app startup (best-effort, never blocks startup) and is also exposed as a manual "Run integrity check" button in the Admin panel.

**Why both automatic + manual?**
- Automatic: catches the problem fast, self-heals
- Manual: gives admins visibility into when/what broke
- Alert email: ensures root cause still gets investigated (we're not silently hiding bugs)

**Audit trail:**
Every auto-fix generates an audit log entry, timestamped with who fixed it (the system). If the same orphan keeps reappearing, the audit log will show it, signaling a different code path is still bypassing the cascade.

### Layer 3: Regression Tests

Added `DataIntegrityServiceTests`:

```csharp
[Fact]
public void FixOrphanedRelationshipsAsync_FindsAndDeletesOrphanedRelationships()
{
    // Setup: person A (soft-deleted), relationship A→B (still live)
    
    // Act
    var result = await integrityService.FixOrphanedRelationshipsAsync();
    
    // Assert: the orphaned relationship is now deleted
    result.Data.Count.Should().Be(1);
    
    var fixed = await ctx.Relationships
        .IgnoreQueryFilters() // Include soft-deleted
        .FirstAsync(r => r.Id == orphanedId);
    fixed.DeletedAt.Should().NotBeNull();
}
```

All 75 Core tests pass.

## Why This Mattered

An orphaned relationship is a **quiet corruption**:
- It doesn't crash the app
- It doesn't appear in the UI (soft-deleted people are hidden)
- But it's a symptom that cascade logic broke somewhere

If another code path (say, an admin delete or an import regression) silently creates orphans, we'd never know until they accumulate and break something downstream.

## Lessons for Data Integrity

1. **Cascade deletes are contracts** — if you promise "delete person A and all their relationships," every code path must enforce it. `DeleteAsync` enforces it; `RollbackAsync` didn't.

2. **Audit logs are your safety net** — every fix gets logged so you can ask "what broke?" instead of just "how do I fix it?"

3. **Automatic healing + alerting beats silent self-healing** — we could have *only* auto-fixed orphans without an alert, but then we'd never know if the root cause is still active. Equally, an alert without auto-fix means users see corruption and admins have to manually fix it every time.

4. **Soft deletes make orphans easier to spot** — hard deletes (actually removing rows) would leave dangling foreign keys that the database would catch. Soft deletes are more elegant for "undo" workflows, but they hide cascading delete failures. Explicit validation compensates.

5. **Test with realistic data shapes** — our synthetic test data was clean. Real genealogy imports are messy (partial data, relationships spanning pre-existing and imported people, rollbacks). Bulk import is now deactivated, but the integrity checks remain because the same shapes can occur from user actions.

## Production Impact

We ran the integrity check against production data:
- **Found:** 3 orphaned relationships (all related to the same June 24 rollback)
- **Fixed:** Soft-deleted all 3
- **No user-visible change:** those relationships were already invisible (pointing to soft-deleted people)

Marc's tree now shows only the correct sibling relationships.

---

**Related:** `Program.cs` startup integrity check (2026-07-09), `DataIntegrityServiceTests` (75 tests passing), and `ClaudeImportServiceRollbackTests` (regression coverage for the rollback fix)
