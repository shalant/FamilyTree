# Multi-Tenant Architecture for Family Trees

**Posted:** July 29, 2026  
**Category:** Architecture & Data Model

## One Database, Many Families

ArborKin uses a **soft multi-tenant** model: one SQL Server database, multiple family "tenants" sharing the same schema.

The core table:
```sql
CREATE TABLE People (
    Id uniqueidentifier PRIMARY KEY,
    FamilyId uniqueidentifier NOT NULL,  -- Foreign key to Family table
    FirstName nvarchar(100) NOT NULL,
    LastName nvarchar(100),
    -- ... other columns
)

CREATE TABLE Family (
    Id uniqueidentifier PRIMARY KEY,
    Name nvarchar(256) NOT NULL,
    CreatedAt datetime2 NOT NULL,
)
```

Every `Person` belongs to exactly one `Family`. Queries are scoped to the authenticated user's family:

```csharp
var familyId = currentUser.FamilyId;
var people = await ctx.People
    .Where(p => p.FamilyId == familyId)
    .ToListAsync();
```

## How We Prevent Cross-Family Leaks

**Claim-based scoping:** The user's `FamilyId` is baked into their auth cookie at sign-in:

```csharp
// AppUserClaimsPrincipalFactory.cs
if (!user.IsSuperUser)
{
    var familyId = await ctx.UserFamilies
        .Where(uf => uf.UserId == user.Id)
        .Select(uf => (Guid?)uf.FamilyId)
        .FirstOrDefaultAsync();
    
    if (familyId.HasValue)
        identity.AddClaim(new Claim("FamilyId", familyId.Value.ToString()));
}
```

**Service-layer checks:** Every query reads the claim and filters:

```csharp
public async Task<List<PersonDto>> GetAllAsync(CancellationToken ct = default)
{
    var familyId = currentUser.FamilyId;
    var isSuperUser = currentUser.IsSuperUser;

    var people = await ctx.People
        .Where(p => isSuperUser || (familyId.HasValue && p.FamilyId == familyId))
        .ToListAsync(ct);

    return people.Select(p => PersonMapper.MapPersonToDto(p, ...)).ToList();
}
```

**Super-user bypass:** Only the one super-user (system admin) can see all data:
```csharp
if (!user.IsSuperUser)
{
    // Regular user: scoped to their family
    var familyId = await GetUserFamilyIdAsync(user.Id);
}
else
{
    // Super-user: no scoping
}
```

## The FamilyId Gap (And How We Fixed It)

We found a critical bug mid-development: **every registered user had `FamilyId == null` in their claims**.

Why? The registration code never created a `UserFamily` row linking the new user to a family.

```csharp
// Old code (broken)
var user = new AppUser { Email = email, UserName = email };
await userManager.CreateAsync(user, password);
// Never called: await ctx.UserFamilies.AddAsync(
//     new UserFamily { UserId = user.Id, FamilyId = familyId });
```

**The consequence:** A user with `null` FamilyId would fall through family scoping checks and accidentally see *all* families' data (or worse, none).

**The fix:**
```csharp
// New code (fixed)
var user = new AppUser { Email = email, UserName = email };
await userManager.CreateAsync(user, password);

// Explicitly create the UserFamily row
var familyId = inviteId.HasValue 
    ? (await ctx.UserInvites.FindAsync(inviteId)).FamilyId
    : (await ctx.Families.OrderBy(f => f.CreatedAt).FirstOrDefaultAsync()).Id;

await ctx.UserFamilies.AddAsync(new UserFamily 
{
    UserId = user.Id, 
    FamilyId = familyId 
});
```

## Audit Trail & Accountability

Every Person, Relationship, and Medium record has audit fields:
```sql
CreatedBy uniqueidentifier NOT NULL,     -- User.Id
CreatedAt datetime2 NOT NULL,
UpdatedBy uniqueidentifier,
UpdatedAt datetime2,
DeletedBy uniqueidentifier,
DeletedAt datetime2 NULL,
```

When a change happens, we log it:
```csharp
_ = auditLog.LogAsync(
    action: "Update", 
    entityType: "Person", 
    entityId: person.Id, 
    userId: currentUser.UserId,
    // ... other fields
);
```

This enables:
- "Who deleted this person?" (audit log)
- "Restore this person if they were deleted by mistake" (soft delete + restore)
- "Who changed what, when?" (activity timeline)

## Lessons Learned

1. **Scoping is your primary defense** — check it at the service boundary, not in the UI. Users can bypass the UI; they can't bypass the service layer.
2. **Never trust null for security** — "if FamilyId is null, they're an admin" is exactly wrong. Use an explicit flag (`IsSuperUser` claim).
3. **Test cross-family reads** — write specific tests that try to read from a *different* family. We have regression tests like `PersonServiceTests.GetAllAsync_NoFamilyIdAndNotSuperUser_ReturnsNoPeople`.
4. **Audit fields are free** — stamp them on every write. The cost is negligible; the value for debugging and compliance is high.

## Next: Per-Family Admin

Currently, Doug is the only admin and sees everything. Next: let families appoint their own admins who can edit/delete within their family only, without seeing other families.

---

**Related:** [[Security — RBAC Implementation]](01-rbac-implementation.md)
