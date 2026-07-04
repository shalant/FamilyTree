# ADR 003: Service Layer Enforcement

**Date:** 2026-07-03  
**Status:** Accepted  
**Context:** FamilyTree is a three-tier app (Web → Core Services → Database). Frontend components must NEVER access DbContext directly.

## Rule

**Frontend components (Blazor pages, admin tabs) MUST NOT:**
- Inject `IDbContextFactory<AppDbContext>`
- Inject `AppDbContext`
- Use `Microsoft.EntityFrameworkCore`
- Write LINQ queries against the database

**All database queries must go through the Core service layer.**

## Why

1. **Separation of concerns:** UI layer shouldn't know about database schema
2. **Testability:** Services can be mocked; DbContext can't easily
3. **Reusability:** Multiple UIs can share the same service logic
4. **Maintainability:** Schema changes only affect services, not scattered UI code
5. **Security:** Queries can be validated/audited in one place

## Implementation Pattern

**Instead of:**
```csharp
@inject IDbContextFactory<AppDbContext> DbFactory
@code {
    private async Task LoadAsync()
    {
        await using var ctx = await DbFactory.CreateDbContextAsync();
        _data = await ctx.SomeEntity.ToListAsync();
    }
}
```

**Do:**
```csharp
@inject ISomeService SomeService
@code {
    private async Task LoadAsync()
    {
        _data = await TryAsync(
            () => SomeService.GetAllAsync(),
            "Loading data");
    }
}
```

This requires:
1. Create `ISomeService` interface in Core
2. Implement the query logic in `SomeService`
3. Register in Program.cs: `builder.Services.AddScoped<ISomeService, SomeService>();`
4. Inject service in component

## Current Violations

⚠️ **These need refactoring:**
- `ActivityTab.razor` — queries UserActivities directly
- `AuditLogTab.razor` — queries AuditLogs directly
- `UsersTab.razor` — queries AppUsers directly
- `AdminOLD.razor` — legacy, should be deleted

## Related

- [ADR 002: Error Handling with BaseComponent](./002-error-handling-with-basecomponent.md)
