# Next Steps — Arborkin

> Opinionated sequence. Each item unblocks the next. Updated: 2026-06-07.

---

## Right now (immediate)

**Hard-restart the app after component changes.**
Hot reload doesn't pick up new `[Parameter]` declarations. Any time you add a parameter
to a Blazor component, stop IIS Express (Shift+F5) and relaunch (F5) to force a full
recompile. The `ActionRow`, `SectionHeading`, and `ExportRow` fixes are already in the
files — they just need a rebuild to take effect.

---

## Next: Auth (the big one)

Everything below depends on knowing who the current user is. Do it in this order —
each sub-step is shippable on its own.

### 1. Add ASP.NET Core Identity
- Install `Microsoft.AspNetCore.Identity.EntityFrameworkCore` NuGet in `FamilyTree.Core`
- Change `AppDbContext` to extend `IdentityDbContext<IdentityUser>` (or a custom
  `ArborkinUser : IdentityUser` that adds `PersonId`, `IsSuperUser`, `FeatureFlags`)
- Run `dotnet ef migrations add AddIdentity` from `src/FamilyTree.Core`
- The `AppUser` placeholder table we created will be superseded by Identity's
  `AspNetUsers` — migrate the columns across or start fresh (no real data in it yet)

### 2. Wire cookie auth + Google OAuth
- `builder.Services.AddAuthentication().AddGoogle(...)` in `Web/Program.cs`
- Client ID and secret go in User Secrets (`dotnet user-secrets set ...`)
- Add Login / Logout pages (scaffold with Identity UI or hand-roll — hand-rolling is
  cleaner for a custom look)
- `app.UseAuthentication(); app.UseAuthorization();` already slots in before
  `app.MapRazorComponents`

### 3. Seed the super-user
- On startup in Development, check if `AspNetUsers` is empty — if so, create one user
  with a known email and `IsSuperUser = true`
- This gives you admin access immediately without a full invite flow

### 4. Replace the `AdminEnabled` flag with real role checks
- `NavMenu.razor`: swap `@if (Config.GetValue<bool>("AdminEnabled"))` →
  `<AuthorizeView Roles="Admin,SuperUser">`
- `Admin.razor`: swap the redirect block → `@attribute [Authorize(Roles="Admin,SuperUser")]`
- Delete the `AdminEnabled` lines from both `appsettings` files

### 5. Populate audit fields from the current user
- Create `ICurrentUserService` — returns the logged-in user's `Guid` from
  `IHttpContextAccessor` or `AuthenticationStateProvider`
- Inject it into `PersonService` and set `CreatedBy`, `UpdatedBy`, `DeletedBy`,
  and `AuditLog.UserId` on every write
- Until this is done, those fields stay `null` — that's fine

### 6. Wire the invite flow
- The `UserInvite` table and schema already exist
- Add `InviteService.CreateInviteAsync(email, familyId, role)` → generates a
  `RandomNumberGenerator` token, saves the row
- Add a `/join?token=...` page that validates the token, creates the user (or links
  an existing one), creates the `UserFamily` row, marks `AcceptedAt`
- Wire email sending last (SendGrid) — the invite still works without it if you
  copy-paste the link manually during development

---

## After auth: photo upload (isolated, can be done any time)

`ProfilePhotoUrl` is currently a plain string. The real flow:
- `MediaUploadZone` → `IBlobStorageService.UploadAsync` → returns a URL → saved to
  `Person.ProfilePhotoUrl`
- The infrastructure (`BlobStorageService`, Azure Blob container) already exists
- This is self-contained — no auth dependency — so it can be done before or after auth

---

## After auth: family scoping

Once you know which family a user belongs to (from `UserFamily`):
- Add `Guid? FamilyId` to service method signatures (or resolve it from
  `ICurrentUserService`)
- Add `.Where(p => p.FamilyId == currentFamilyId)` to `PersonService.GetAllAsync`
- This is the multi-tenant payoff — users only see their own family's tree

---

## Deferred (don't start yet)

These are real but not urgent — starting them now would be premature:

| Item | Why deferred |
|------|-------------|
| GEDCOM import/export | Nice to have; no user demand yet |
| AI document import | Depends on stable data model |
| Timeline view | Phase 2 polish |
| Real-time presence | Phase 3 complexity |
| Mobile layout | Do after tree is feature-complete |
| Performance / virtual rendering | Only matters at 200+ nodes |

---

## Decision still open

**Is Viewer a real role or just unauthenticated access?**
If all family members will edit, Viewer adds complexity for no benefit right now.
Recommendation: skip Viewer in the first auth pass — add it only when someone
actually needs read-only access.
