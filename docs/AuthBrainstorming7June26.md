# Auth Brainstorming — 7 June 2026

> Pre-implementation design notes. Resolve these decisions before writing auth code.

---

## Role Hierarchy

```
Super-user (owner)   — cross-family, all permissions, seeds admins
Admin (1-2/family)   — manage members, restore soft-deleted records, view audit log
Member               — full CRUD on people and relationships
Viewer               — read-only, browse tree only
```

**Open question:** Is Viewer a real role or just unauthenticated access? If family members always edit, Viewer may not be needed yet.

**Admin promotion:** Super-user grants it manually in the admin screen. No self-promotion, no auto-elevation. Admins can manage Members but cannot create other Admins — only the super-user can. Keeps the privilege chain clean.

---

## The Person↔User Link (most important, easy to miss)

A user who logs in *is* probably a person in the family tree. Decision needed:

- Add a nullable `PersonId` FK on the Identity user — optional link, not required
- Enables "you are Douglas, born 1978" type personalisation on login
- Some users (super-user) may not be in the tree at all
- Recommendation: link is optional, not enforced

---

## How People Join

**Recommendation: invite-only.** This is private family data — open registration is inappropriate.

Options considered:
| Approach | Notes |
|----------|-------|
| **Invite-only** (recommended) | Admin sends email link with time-limited token. Safest. |
| Request access | Anyone registers but lands in "pending" until admin approves |
| Open + family code | Anyone registers, needs a code to join a tree |

Invite tokens need: recipient email, expiry timestamp, family ID, role to grant, used/cancelled flag.

---

## Soft Delete Design

Decided: **yes, soft delete.**

Three things to implement correctly:

1. Add `DeletedAt DateTime?` and `DeletedBy Guid?` to `Person`, `Relationship`, and `Medium`
2. Use **EF Core Global Query Filters** so every query automatically excludes deleted records:
   ```csharp
   modelBuilder.Entity<Person>().HasQueryFilter(p => p.DeletedAt == null);
   ```
3. **Cascade behaviour:** if a Person is soft-deleted, hide Relationships where either party is deleted — but do NOT cascade-soft-delete the Relationship rows themselves. Makes restore cleaner.

Admin screen can view and restore soft-deleted records.

---

## Audit Log

Current `CreatedBy`/`UpdatedBy` fields are a good start but insufficient for an admin dashboard.

Add a dedicated `AuditLog` table:

```
AuditLog
  Id            Guid
  UserId        Guid
  Action        string    (Create / Update / Delete / Login / RoleChange / Restore)
  EntityType    string    (Person / Relationship / Medium / User)
  EntityId      Guid?
  Timestamp     DateTime
  IpAddress     string?
  OldValue      string?   (JSON snapshot)
  NewValue      string?   (JSON snapshot)
```

**Decide what to log:** login attempts, all CRUD, or just destructive actions. More is better but adds overhead. Recommend: all destructive actions + logins + role changes at minimum.

---

## Feature Flags Per User

Requirement: disable features per user, lock out users, track donor status, daily CRUD cap.

**Recommendation: JSON column on the user model** — fast, no extra table, easy to extend:

```json
{
  "canEdit": true,
  "isLocked": false,
  "dailyCrudCap": 10,
  "isDonor": false,
  "disabledFeatures": []
}
```

Alternative: a `UserPermission` table with one row per feature — more queryable but more overhead. Fine to start with JSON and migrate later if needed.

---

## Daily CRUD Cap

- Store a `UserActivity` table: `UserId`, `Date`, `ActionCount`
- Check on every write operation, reject if over cap
- Super-user and Admins are exempt
- Decide whether reads count (recommendation: no)
- Users can request a cap increase (manual super-user override for now)

---

## Session & Auth Provider

**Cookie auth, not JWT.** Blazor Server runs over a persistent SignalR connection — cookies are the natural fit, not stateless JWT.

**Login providers:**
- Google OAuth — primary, least friction for family members
- Email/password — fallback for non-Google users
- Skip Apple and Microsoft for now

**MFA:**
- Required for super-user and admins
- Optional for members
- Not required for viewers

---

## Visibility by Relationship Depth

Idea: limit a user's visible tree to people within N relationship links of themselves.

- Complex to implement — requires BFS on the graph per request
- Deferred to Phase 3
- Worth keeping the idea: could be a per-user setting in feature flags (`"visibilityDepth": 3`)

---

## Other Deferred Items

| Feature | Notes |
|---------|-------|
| Real-time presence | Who else is browsing concurrently — Phase 3 |
| Donor flag | Simple boolean in feature flags, add when ready |
| Public/shareable view | `?focus=` URL already exists, just needs an auth bypass route |
| Comment/memory system | Phase 3 collaboration feature |
| Instant messaging | Phase 3, significant complexity |

---

## Key Architectural Question (resolve before starting)

> Is this one family tree (one `FamilyId`) with one super-user, or will multiple independent families each have their own super-user eventually?

The `Family` table already supports multi-tenant. But the role model needs to decide whether "Admin" is scoped to a family or global. With one family right now it doesn't matter — but name and scope roles correctly from day one to avoid renaming later.

**Recommendation:** scope Admin to a family (`UserFamily` join table with a `Role` column). Super-user is a global flag on the user, not family-scoped.

```
UserFamily
  UserId    Guid  FK → AspNetUsers
  FamilyId  Guid  FK → Family
  Role      string  (Admin / Member / Viewer)
  JoinedAt  DateTime
```

---

## Implementation Order (when ready)

1. Add ASP.NET Core Identity tables (migration)
2. ~~Create `UserFamily` join table and `AuditLog` table (migration)~~ ✅ done in pre-auth pass
3. ~~Add `DeletedAt`/`DeletedBy` to Person, Relationship, Medium + EF Global Query Filters (migration)~~ ✅ done
4. Add nullable `PersonId` to Identity user (migration) — `AppUser.PersonId` already exists; link to Identity user on creation
5. Wire Google OAuth + email/password login
6. Seed super-user account
7. Add `[Authorize]` to pages, redirect to login
8. Build invite flow — `UserInvite` table already exists; wire email send + accept endpoint
9. ~~Build admin dashboard (audit log, user management, soft-delete restore)~~ ✅ done in pre-auth pass
10. Add feature flags + daily CRUD cap — `FeatureFlags` JSON column already on `AppUser`; wire enforcement

---

## Pre-Auth Iteration — What Was Built (2026-06-07)

These items from the implementation order were completed ahead of auth as a standalone iteration:

**Schema (migration `PreAuthIteration`):**
- `DeletedAt DateTime?` / `DeletedBy Guid?` on Person, Relationship, Medium
- EF Core Global Query Filters on all three — invisible to all normal queries
- `AppUser` table (lightweight POCO — not Identity yet): Id, Email, DisplayName, PersonId, IsSuperUser, FeatureFlags (JSON), CreatedAt, LastLoginAt
- `UserFamily` join table: UserId, FamilyId, Role, JoinedAt
- `UserInvite` table: Id, Email, FamilyId, RoleToGrant, Token, ExpiresAt, AcceptedAt, CancelledAt, CreatedBy, CreatedAt
- `AuditLog` table: Id, UserId?, Action, EntityType, EntityId?, Timestamp, IpAddress?, OldValue, NewValue
- `UserActivity` table: Id, UserId?, Date, ActionCount — unique on (UserId, Date)

**Service layer:**
- `PersonService.DeleteAsync` → soft delete (sets `DeletedAt`; does not remove row or relationships)
- `PersonService.RestoreAsync` → clears `DeletedAt` / `DeletedBy` via `IgnoreQueryFilters()`
- `PersonService.GetDeletedAsync` → returns soft-deleted people for admin UI
- `PersonService.GetAllAsync` → filters relationships in-memory to exclude those where either party is deleted
- `IAuditLogService` / `AuditLogService` → fire-and-forget audit writes on Create/Update/Delete/Restore; exceptions swallowed

**Admin UI (`/admin`):**
- Dashboard tab: stat cards (people, deleted count, audit entries today, users, families, relationships, pending invites), recent audit activity feed, quick-nav panel
- Deleted tab: soft-deleted people table with per-row Restore button
- Users tab: `AppUser` list (empty until Identity is wired)
- Audit log tab: filterable by action and entity type; 500-row cap; local time display
- Activity tab: `UserActivity` daily counts (empty until auth is wired)

**Access gating:**
- `AdminEnabled: false` in `appsettings.json` (production default — page redirects to `/`)
- `AdminEnabled: true` in `appsettings.Development.json`
- Nav link hidden when flag is false
- **When auth lands:** replace with `<AuthorizeView Roles="Admin,SuperUser">` in NavMenu + `@attribute [Authorize(Roles="Admin,SuperUser")]` on Admin.razor — no other changes needed
