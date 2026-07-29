# AuthService Refactor Plan

**Goal:** Convert `IAuthService` to return `ServiceResponse<T>` instead of custom `AuthResult`/`InviteResult` records, enabling consistent error handling via `BaseComponent.TryAsync`.

**Status:** Planned, not started
**Estimated effort:** 4-6 hours
**Dependencies:** None (standalone refactor)

---

## Problem Statement

**Current state:**
- `AuthService` returns custom records: `AuthResult`, `InviteResult`
- `IAuthService` does NOT follow the documented pattern (all other services return `ServiceResponse<T>`)
- **No try/catch** in `AuthService` methods — exceptions bubble up raw
- **No `BaseComponent.TryAsync` wrapper** can be used on auth calls (it's typed for `Func<Task<ServiceResponse<T>>>`)
- `Register.razor` has **zero error handling** for registration failures

**Live gap found:**
- User hits exception during registration → raw Blazor error UI instead of friendly toast

---

## Interface Changes

**Current:**
```csharp
Task<AuthResult> RegisterAsync(...);
Task<AuthResult> LinkPersonAsync(...);
Task<InviteResult> CreateInviteAsync(...);
Task<AuthResult> CancelInviteAsync(...);
// etc.
```

**Target:**
```csharp
Task<ServiceResponse<Guid>> RegisterAsync(...);  // returns user ID
Task<ServiceResponse> LinkPersonAsync(...);       // no data return
Task<ServiceResponse<Guid>> CreateInviteAsync(...); // returns invite ID
Task<ServiceResponse> CancelInviteAsync(...);    // no data return
// etc. — see mapping table below
```

---

## Method Mapping: AuthResult → ServiceResponse<T>

| Method | Old Return | New Return | Notes |
|--------|-----------|-----------|-------|
| `RegisterAsync` | `AuthResult(Success, Error?, UserId?)` | `ServiceResponse<Guid>` | Data is user ID |
| `LinkPersonAsync` | `AuthResult(Success, Error?)` | `ServiceResponse` | No data |
| `CreateInviteAsync` | `InviteResult(Success, Error?, Id?)` | `ServiceResponse<Guid>` | Data is invite ID |
| `CancelInviteAsync` | `AuthResult(Success, Error?)` | `ServiceResponse` | No data |
| `ValidateInviteAsync` | `UserInvite?` | `ServiceResponse<UserInvite?>` | Nullable return |
| `GetPendingInvitesAsync` | `List<UserInvite>` | `ServiceResponse<List<UserInvite>>` | Wrap list |
| `RequestPasswordResetAsync` | `void` (no validation) | `ServiceResponse` | Add error handling |
| `ResetPasswordAsync` | `AuthResult(Success, Error?)` | `ServiceResponse` | No data |
| `IsResetRequestValidAsync` | `bool` | `ServiceResponse<bool>` | Wrap bool |
| `LinkUserToTreeAsync` | `AuthResult(Success, Error?, PersonId?)` | `ServiceResponse<Guid?>` | Nullable person ID |
| `CreateUnlinkedPersonAsync` | `AuthResult(Success, Error?, PersonId?)` | `ServiceResponse<Guid>` | Person ID |
| `GetLinkedPersonIdsAsync` | `HashSet<Guid>` | `ServiceResponse<HashSet<Guid>>` | Wrap set |
| `EnsureUserFamilyAsync` | `void` (no error handling) | `ServiceResponse` | Add error handling |

---

## Implementation Checklist

### Phase 1: Interface & Core Implementation
- [ ] Add `using FamilyTree.Shared;` to `IAuthService.cs` and `AuthService.cs`
- [ ] Update `IAuthService` interface with all `ServiceResponse<T>` return types
- [ ] Refactor `AuthService` implementation:
  - [ ] Wrap every method in `try/catch`
  - [ ] Return `ServiceResponse.Ok(data)` / `ServiceResponse.Fail(message)`
  - [ ] Add `ILogger<AuthService>` to constructor
  - [ ] Log all exceptions at `.LogError(ex, "...")`
- [ ] Delete `AuthResult` and `InviteResult` records from `IAuthService.cs`

**Note:** `StoryInviteService.cs` also references `InviteResult` — will need updates in Phase 2

### Phase 2: Call Site Updates (separate PR or branch)

**Files to update:**
1. **Register.razor** — Wrap `RegisterAsync` call in `BaseComponent.TryAsync` instead of zero error handling
2. **ResetPassword.razor** — Update `.Success` checks to new signature
3. **ForgotPassword.razor** — Replace direct call with `BaseComponent.TryAsync`
4. **LinkToTreeModal.razor** — Replace ad-hoc try/catch with `BaseComponent.TryAsync`
5. **UsersTab.razor** (Admin) — Update `.Success` checks
6. **AdminOLD.razor** — Update `.Success` checks
7. **Home.razor** — Update focus person calls
8. **Program.cs** (if needed) — Verify dependency injection handles `ILogger<AuthService>`

### Phase 3: Cascading Updates

**Services that depend on AuthService:**
- [ ] `StoryInviteService.cs` — References `InviteResult.Id`; update to use `ServiceResponse<Guid>.Data`

---

## Technical Details

### ServiceResponse API (reference)
```csharp
// Generic version for operations returning data
public class ServiceResponse<T>
{
    public T? Data { get; init; }
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    
    public static ServiceResponse<T> Ok(T data) => new() { Success = true, Data = data };
    public static ServiceResponse<T> Fail(string message) => new() { Success = false, Message = message };
}

// Non-generic version for operations with no return data
public class ServiceResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    
    public static ServiceResponse Ok() => new() { Success = true };
    public static ServiceResponse Fail(string message) => new() { Success = false, Message = message };
}
```

### BaseComponent.TryAsync Pattern (reference)
```csharp
// In Razor components inheriting from BaseComponent
await TryAsync(async () => {
    var result = await AuthService.RegisterAsync(...);
    // No need to check .Success — TryAsync handles it and shows a toast on failure
    return result;
});
```

---

## Known Complications

1. **Nullable returns** — `ValidateInviteAsync` returns `UserInvite?` (nullable). Need `ServiceResponse<UserInvite?>` to distinguish "invite not found" from "invite found but null."

2. **InviteResult.Id vs ServiceResponse.Data** — `StoryInviteService` accesses `.Id` property which doesn't exist on `ServiceResponse<Guid>`. Must change to `.Data`.

3. **Error message consistency** — Currently `AuthResult` carries a single error message. `ServiceResponse` also carries `Message`. No conflict, but ensure messages are consistent across both patterns during transition.

4. **Method overloads** — Some methods like `RequestPasswordResetAsync` currently have no validation/error handling. Adding error cases changes the contract.

---

## Why This Matters

- **Consistency:** All services follow the same pattern
- **Safety:** Every auth call gets try/catch + logging
- **UX:** Users see friendly toasts, not raw exceptions
- **Testability:** ServiceResponse makes testing assertions clearer
- **Maintainability:** One error-handling pattern across the codebase

---

## Rollback Plan

If issues arise:
1. Revert `IAuthService.cs` and `AuthService.cs` to original commit
2. Keep call-site updates (they're backwards-compatible with the old interface)
3. The original `AuthResult`/`InviteResult` records remain in the interface until all call sites are updated

---

## Related Tasks

- **Role-based authorization (Line 49):** Should be tackled AFTER this refactor, since auth role checks will want to use the new `ServiceResponse` pattern
- **Semantic duplicate detection (Line 35):** Independent; can run in parallel if needed
- **Azure deployment to arborkin.com:** Independent planning task

