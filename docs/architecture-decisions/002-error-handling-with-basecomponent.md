# ADR 002: Error Handling with BaseComponent

**Date:** 2026-07-03  
**Status:** Accepted  
**Context:** Frontend components were failing silently when service calls returned errors, causing users to be redirected to dead-end pages (e.g., `/register?invite=` with empty token). The StoryRespond flow demonstrated this critical UX gap.

## Problem

1. **Silent failures:** Service call errors weren't visible to users
2. **Broken redirects:** StoryRespond was redirecting to invalid pages when UserInviteId was null
3. **Inconsistent patterns:** Different components handled errors differently
4. **User frustration:** Users couldn't understand why they ended up on broken pages

Example from StoryInviteService: The service returned `Success=false` when reusing an existing invite, but the ID was still valid. The component checked `.Success` and discarded the valid ID, breaking the redirect.

## Solution

Create `BaseComponent : ComponentBase` with `TryAsync<T>()` and `TryAsync()` wrapper methods that:
1. Execute the service call safely
2. Show error toasts automatically on failure
3. Return data on success, default(T) on failure
4. Handle exceptions and log them

### Implementation

```csharp
public abstract class BaseComponent : ComponentBase
{
    [Inject] protected ToastService ToastService { get; set; } = null!;

    protected async Task<T?> TryAsync<T>(
        Func<Task<ServiceResponse<T>>> call,
        string action = "Operation")
    {
        try
        {
            var result = await call();
            if (!result.Success)
            {
                ToastService.Error(result.Message ?? $"{action} failed");
                return default;
            }
            return result.Data;
        }
        catch (Exception ex)
        {
            ToastService.Error($"{action} failed: {ex.Message}");
            return default;
        }
    }

    protected async Task<bool> TryAsync(
        Func<Task<ServiceResponse>> call,
        string action = "Operation")
    {
        // Similar pattern, returns bool
    }
}
```

### Usage

**Before:**
```csharp
var result = await StoryInviteService.SubmitResponseAsync(_response);
if (!result.Success)
{
    _error = result.Message;
    ToastService.Error(result.Message);
    return;
}
```

**After:**
```csharp
_story = await TryAsync(
    () => StoryInviteService.SubmitResponseAsync(_response),
    "Saving your memory");
if (_story != null)
{
    // proceed
}
```

## Benefits

✅ Guaranteed error visibility (toast always shown on failure)  
✅ Consistent error handling across all components  
✅ Cleaner, more readable component code  
✅ Single point of control for error UI behavior  
✅ Prevents silent failures and dead-end redirects  

## Adoption

All frontend components should inherit from `BaseComponent`:
- ✅ StoryRespond.razor
- ✅ DeletedTab.razor
- ✅ DashboardTab.razor
- ⚠️ ActivityTab.razor (still using DbContext)
- ⚠️ AuditLogTab.razor (still using DbContext)
- ⚠️ UsersTab.razor (still using DbContext)
- ⏳ Other admin tabs (MessagesTab, ImportsTab, etc.)

## Related

- [ADR 003: Service Layer Enforcement](./003-service-layer-enforcement.md)
