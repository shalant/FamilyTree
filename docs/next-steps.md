# Next Steps — Arborkin

> Opinionated sequence. Each item unblocks the next. Updated: 2026-06-08.

---

## Right now (immediate)

**Run the Identity migration.**
Stop IIS Express (Shift+F5), then from `src/FamilyTree.Core`:

```
dotnet ef migrations add AddIdentity
dotnet ef database update
```

This drops the old `AppUsers` table and creates `AspNetUsers` + all Identity tables
(`AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, etc.) plus the new columns:
`Family.IsPublic`, `Family.RequireApproval`, `AppUser.PersonClaimStatus`,
`Person.IsMinorOverride`, and the unique filtered index on `AppUser.PersonId`.

After migration, restart the app (F5). DevAuth is already wired — you'll be
auto-logged-in as Admin in development, no credentials needed.

---

## Next: Auth flow

### 1. Google OAuth + email/password
- `builder.Services.AddAuthentication().AddGoogle(...).AddCookie()` in `Program.cs`
- Google Client ID/Secret → `dotnet user-secrets set`
- Email/password uses ASP.NET Identity's built-in hashing — nothing extra needed

### 2. Register / Login / Logout pages
- Open registration — anyone can sign up (email + password OR Google)
- `UserName = Email` on `UserManager.CreateAsync` — Identity requires it
- Set `CreatedAt = DateTime.UtcNow` on create
- Login page handles both cookie auth schemes (Google redirect + local password)
- Logout: `SignOutAsync` both cookie and Google schemes

### 3. Email verification
- `UserManager.GenerateEmailConfirmationTokenAsync` → send via SendGrid
- Unverified accounts can log in but see a banner; gate family access behind `EmailConfirmed`
- In development: log the token URL to console instead of sending email

### 4. Forgot / reset password
- Two pages: "Forgot password" (enter email) and "Reset password" (token + new password)
- `UserManager.GeneratePasswordResetTokenAsync` → email link
- `UserManager.ResetPasswordAsync` on submit
- Google-only users don't have a password; show "sign in with Google" instead

### 5. Seed super-user on startup
- Development only: if `AspNetUsers` is empty, create one user from config with `IsSuperUser = true`
- Assign the "SuperUser" Identity role so `[Authorize(Roles="SuperUser")]` works

### 6. Replace DevAuthHandler
- Once login works, flip `DevAuth:Enabled` to `false` in `appsettings.Development.json`
- The real auth scheme takes over; `DevAuthHandler` stays in the codebase for future use

---

## After login: onboarding flow

New users who just registered land on an onboarding page. Two paths:

**Create a family** → names their family, creates a `Family` row, gets Admin role via `UserFamily`

**Find my node (public search wizard)**
1. User searches by name on families where `Family.IsPublic = true`
2. Results show deceased people fully; living adults by name only; minors hidden
3. "This is me" → sets `AppUser.PersonId` + `PersonClaimStatus = Pending`
4. Family admin sees the claim in `/admin → Users` tab and approves/rejects
5. On approval: `PersonClaimStatus = Approved`, `UserFamily` row created with Member role
6. `Family.RequireApproval = false` skips step 4–5 and auto-approves

**Enter invite code** → validates token, creates `UserFamily` row, skips approval

---

## After onboarding: post-auth wiring

Once you know who the user is and which family they belong to:

- `ICurrentUserService` — resolves current `AppUser` from `ClaimsPrincipal`
- Populate `CreatedBy` / `UpdatedBy` / `DeletedBy` / `AuditLog.UserId` on every write
- `UserActivity` daily count incremented on every mutation
- Family scoping: add `.Where(p => p.FamilyId == currentFamilyId)` to `PersonService.GetAllAsync`
- Data visibility tier applied at service/mapper layer (public vs member vs admin)

---

## Admin: user management

Super-admin user deletion in `/admin → Users` tab:
- `UserManager.DeleteAsync(user)` — cascades to `UserFamily`, nulls FKs in audit/activity
- Approve/reject pending claims: set `PersonClaimStatus`, create/skip `UserFamily` row

---

## Deferred (don't start yet)

| Item | Why deferred |
|------|-------------|
| GEDCOM import/export | Depends on stable data model |
| AI document import | Phase 4 |
| Timeline view | Phase 6 polish |
| Real-time presence | Phase 3+ complexity |
| Mobile layout | After feature-complete |
| Performance / virtual rendering | Only matters at 200+ nodes |
| Photo upload (real blob) | Self-contained, any time after auth |

---

## Decision still open

**Who can set `Family.IsPublic`?**
Recommend: family Admin or Super-user only. A member shouldn't be able to make the
family discoverable by strangers without the admin's consent.
