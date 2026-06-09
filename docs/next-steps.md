# Next Steps — Arborkin

> Opinionated sequence. Each item unblocks the next. Updated: 2026-06-09.

---

## Right now (immediate)

**Add the performance index migration.**

Auth is live and queries are now running against real data. The three soft-delete tables
(People, Relationships, Medium) all filter by `DeletedAt IS NULL` on every page load but
have no index on that column. Add this migration before load grows:

```bash
# From repo root
dotnet ef migrations add AddPerformanceIndexes \
    --project src/FamilyTree.Core \
    --startup-project src/FamilyTree.Web
```

Then edit the generated `Up()` to use `migrationBuilder.Sql(...)` for filtered indexes
(EF's index API doesn't support `WHERE` clauses):

```csharp
migrationBuilder.Sql(@"
    CREATE INDEX IX_People_Family_Active
        ON People (FamilyId, LastName, FirstName) WHERE DeletedAt IS NULL;
    CREATE INDEX IX_Relationships_A_Active
        ON Relationships (PersonAId) WHERE DeletedAt IS NULL;
    CREATE INDEX IX_Relationships_B_Active
        ON Relationships (PersonBId) WHERE DeletedAt IS NULL;
    CREATE INDEX IX_AuditLog_Entity
        ON AuditLog (EntityType, EntityId, Timestamp DESC);
");
```

And mirror it in `Down()` with `DROP INDEX IF EXISTS`. Commit and `workflow_dispatch` to deploy.

---

## Next: populate write-path audit fields

Every `PersonService.CreateAsync` / `UpdateAsync` / `DeleteAsync` call should stamp
`CreatedBy` / `UpdatedBy` / `DeletedBy` from the current user and increment `UserActivity`.
Auth is wired; the service calls just need the current userId injected.

1. Add `ICurrentUserService` — thin wrapper around `IHttpContextAccessor` that reads the
   `ClaimTypes.NameIdentifier` claim and returns `Guid? UserId`
2. Inject into `PersonService`, `RelationshipService`, `MediumService`
3. Set `CreatedBy = currentUser`, `UpdatedBy = currentUser` on every write
4. Call `AuditLogService.LogAsync(userId, action, entityType, entityId, old, new)` — already exists
5. Call `UserActivityService.IncrementAsync(userId)` on every mutation

---

## After that: family scoping

All service queries currently load all non-deleted records regardless of which family the
user belongs to. Add scoping once write-path fields are stamped (otherwise you can't tell
which family owns a record):

- Resolve `FamilyId` from claims (add it to `AppUserClaimsPrincipalFactory`)
- Add `.Where(p => p.FamilyId == currentFamilyId)` to `PersonService.GetAllAsync`
  and `RelationshipService.GetAllAsync`
- The seeder already creates a "My Family" row and assigns all seed people to it — scoping
  will work immediately without data migration

---

## Testing

The test projects exist (`tests/FamilyTree.Core.Tests`). Good first targets:

**Integration tests (xUnit + real DB):**
- `PersonService.CreateAsync` — happy path, duplicate name, missing required field
- `PersonService.DeleteAsync` — soft delete + restore round-trip
- `RelationshipService` — canonical ordering, duplicate prevention, former spouse transition

**Component tests (bUnit):**
- `PersonForm` — required field validation, date range logic
- `FamilyTreeCanvas` — renders correct number of nodes for a seeded 5-person tree

Run with: `dotnet test FamilyTree.sln`

---

## Email flow (deferred until needed)

The `PasswordResetRequest` table exists. When you're ready:

1. Add SendGrid NuGet + `IEmailService` abstraction
2. Forgot password: `UserManager.GeneratePasswordResetTokenAsync` → SendGrid email with link
3. Reset password page: `UserManager.ResetPasswordAsync` on submit
4. In dev: log the token URL to console instead of sending email
5. Email verification on registration follows the same pattern

---

## Deferred

| Item | Why deferred |
|------|-------------|
| GEDCOM import/export | Stable data model needed first |
| AI document import | Phase 4 — after core is solid |
| Timeline view | Phase 6 polish |
| Find-my-node wizard | Phase 2d — low priority until public sharing needed |
| Real-time presence | Phase 3+ complexity |
| Mobile layout | After feature-complete |
| Performance / virtual rendering | Only matters at 200+ nodes |
| MFA | Only when admin accounts feel like a target |
| Custom domain + SSL | When you're ready to share publicly |
