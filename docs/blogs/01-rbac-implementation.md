# Implementing Role-Based Access Control in a Family Tree App

**Posted:** July 29, 2026  
**Category:** Security & Authorization

## The Problem: Unequal Write Permissions

ArborKin (our family tree app) had a security gap that only became obvious after 5+ family members started using it. The issue: **any authenticated member could edit or delete any other family member's person record, relationship, or story** — even if they'd just joined an hour ago.

From a code perspective, the gap was real but subtle:
- Family scoping worked correctly: members couldn't touch *another family's* data
- Role checks existed, but only at the read layer: `IsSuperUser` gates the "see everything" bypass
- The create/update/delete service methods had *zero* authorization checks
- Everyone got a flat `"Member"` role at registration; the `"Admin"` role plumbing existed in config but wasn't used anywhere

This was acceptable for the MVP ("everyone I invite will be trustworthy and technical") but became a liability once real people joined.

## The Solution: Admin vs. Member Distinction

We implemented a proper Admin/Member role split:

1. **Add `IsAdmin` to the model** — new boolean on `AppUser`
2. **Emit the Admin claim** — `AppUserClaimsPrincipalFactory` reads `IsAdmin` and adds it to the auth cookie
3. **Gate high-leverage operations** — `UpdateAsync` and `DeleteAsync` on `Person` and `Relationship` now check `if (!currentUser.IsSuperUser && !currentUser.IsAdmin) return Fail()`
4. **Database migration** — new column; existing rows default to `IsAdmin = false`

The key insight: authorization checks belong **at the service boundary**, not sprinkled through domain logic. It's the one place every caller passes through.

## Testing & Regression Safety

We added two types of tests:

**Negative tests** (happy path already exists):
```csharp
[Fact]
public void DeleteAsync_RegularMember_ReturnsForbidden()
{
    var fakeUser = new FakeCurrentUserService { IsSuperUser = false, IsAdmin = false };
    var service = new PersonService(..., fakeUser);
    
    var result = await service.DeleteAsync(personId);
    
    result.Success.Should().BeFalse();
    result.Message.Should().Contain("permission");
}
```

**Test infrastructure update** — `FakeCurrentUserService` now has `IsAdmin` property; test helpers set `IsAdmin = true` by default so existing tests don't break.

## Lessons for Feature Flags and Authorization

A few things we learned:

1. **Authorization is security, not convenience** — don't gate it at the UI layer. Service methods must assume the user is hostile.
2. **Exact matches on permission checks** — "is admin OR is super-user" is clearer than stacking conditions. Abstract it to `CanModify()` if you're using it in multiple places.
3. **Keep roles simple early** — we added exactly two: Super + Admin. The temptation to add "Curator", "Viewer", "Editor" is real, but each multiplies test cases. Start with the minimum.

## Next: Invite-Only Role Assignment

Admins can now be designated, but the UI for assigning them doesn't exist yet. Next step: add a toggle in the "User Management" admin panel so Doug can promote trusted curators without needing code changes.

---

**PR #18:** [shalant/FamilyTree/pull/18](https://github.com/shalant/FamilyTree/pull/18)
