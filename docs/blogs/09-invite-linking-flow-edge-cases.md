# Building a Duplicate-Proof Invite & Linking Flow: Edge Cases in Genealogy

**Posted:** July 29, 2026  
**Category:** UX & User Workflows

## The Scenario: Inviting Someone to Write a Story

ArborKin's story invite flow is built for real family dynamics:

1. **Doug** invites his sister **Dara** with a link to "write a story about Bill (father)"
2. Dara receives the email, clicks the link, and lands on `/story/respond/{token}`
3. Before she can write the story, she needs to sign up and **link herself to the tree**
4. The flow asks: "Are you Bill?" (no) → "Who are you related to?" → "Bill" → "You're Bill's child" → create Dara's person
5. Now Dara exists on the tree, linked to Bill, and can write her story

Sounds simple. In practice, it revealed four edge cases that broke either the UX or the data model.

## Edge Case 1: "Bill" Already Exists Under Multiple Spellings

**The problem:**
- In an earlier session, we manually entered "Bill" as Bill S.
- This session, we entered "Bill" again by hand (typo? different name? unclear)
- Now we have Bill S. (original) and Bill (new)
- When Dara signs up and the flow asks "Who is Bill?", which one should we pick?

**Real-world cause:** Genealogy is messy. The same person goes by multiple names ("Bill" vs "William"), has typos in different sources ("Rosenberg" vs "Rosenfeld"), or appears under maiden and married names.

**Solution we tried (naive):** Exact name match only
- Works for 80% of cases
- Fails for "Jon" vs "John", "William" vs "Bill", nickname variations

**Solution we built:** [Fuzzy name matching with Levenshtein distance](02-fuzzy-name-matching.md) (80% similarity threshold)
- Exact matches ranked first
- Fuzzy matches ranked second
- Conservative threshold to minimize false positives

**UI change:** When multiple matches appear (exact + fuzzy), show the exact matches first, then fuzzy with a "similar name" label:

```
"Is Bill Small already on this tree?"
 
- Bill Small (exact match) ✓ Pick this
- William Small (similar name)
- Jill Small (similar name)
```

## Edge Case 2: The Same Person Registered Under Two Accounts

**The problem:**
- Account A: Email `test@example.com`, linked to "Elliot Rosenberg" person
- Account B: Email `test2@example.com`, linked to "Elliot Rosenberg" person (same person, two accounts)
- Now two different users claim to own the same family tree node

**Real-world cause:** This shouldn't happen, but it can if:
- A person signs up twice (forgot they already registered)
- An admin creates an account for someone who already exists
- A bulk import creates a duplicate, then later a person signs up under the same name

**What broke:** When Account B tried to link to "Elliot Rosenberg" (who is already claimed by Account A), the app threw:
```
DbUpdateException: Cannot insert duplicate key row in unique index 'IX_AspNetUsers_PersonId'
```

A raw database error, not a clean message.

**Solution:** Pre-validation in `LinkUserToTreeAsync`:

```csharp
public async Task<ServiceResponse<bool>> LinkUserToTreeAsync(Guid userId, Guid personId)
{
    // Check if this person is already claimed by a different user
    var alreadyClaimed = await ctx.Users
        .AnyAsync(u => u.PersonId == personId && u.Id != userId);
    
    if (alreadyClaimed)
        return ServiceResponse.Fail(
            $"This person is already linked to another account. " +
            $"If this is you, sign in with that account instead.");
    
    // ... proceed with linking
}
```

**UI change:** The "is this you?" person-picker now filters out already-claimed people:

```csharp
var availablePeople = people
    .Where(p => !alreadyClaimedIds.Contains(p.Id))
    .ToList();
```

Only people not yet linked to any account appear in the picker.

**Exception:** Story subjects are *allowed* to already have their own account (different story). Only the linking flow needs this guard.

## Edge Case 3: A Person Without a Recorded Parent Structure

**The problem:**
- Dara signs up and links herself: "I'm Bill's child"
- Bill exists, but Bill has no recorded parents
- The tree layout algorithm has no anchor for Bill's generation, so Dara's generation is ambiguous
- Visual result: Dara floats, detached, or anchors to a random unrelated root

**Root cause:** The layout engine works best when people have clear parent/generation relationships. A person with no parents can be placed, but their children's positioning depends on guessing the generation depth.

**Solution attempted (current UX):**
- If Bill has no parents, ask Dara: "Does Bill have a parent on this tree?" → picker
- If yes, create Bill's parent link
- If no, place Bill as a root, and Dara as his child

**Regression test:**
```csharp
[Fact]
public void LinkingToAnOrphanRoot_PlacesPersonCorrectly()
{
    // Setup: Bill exists, no recorded parents
    // Act: Dara links as Bill's child
    // Assert: Dara and Bill both render, Dara below Bill
}
```

This works, but it's clunky UX. A smoother version would auto-detect the gap and offer to fill it:

```
"Bill doesn't have a recorded parent on this tree. 
 Would you like to add one so Bill's generation is clear?"
```

We didn't build this because it's **soliciting data the user might not know**, which violates the "don't assume genealogy" principle. Better to leave the gap than guess incorrectly.

## Edge Case 4: The Person Being Linked Already Has a Different Relationship to the User

**The problem:**
- Dara signs up and picks "Bill" as her anchor: "I'm Bill's child"
- But Doug (who invited her) has Bill marked as his **sibling** (wrong relationship, typo in data entry)
- Now Dara is Bill's child, but Bill is Doug's sibling — genealogically inconsistent

**Real-world cause:** Messy data entry or people misremembering relationships during setup

**Current handling:** None. We create the relationship as-requested and trust the user.

**Why we didn't "fix" it:** Relationships are often complicated (step-siblings, remarriages, informal adoptions). Detecting "inconsistency" requires understanding genealogical rules, which are culturally specific and not our job to enforce. Better to let the user create what they're confident about and let them fix it later if they realize the first entry was wrong.

**Future improvement:** An optional post-link review step:
```
"You're now linked as Bill's child. 
 Bill is currently marked as Doug's sibling. 
 Does that sound right?"
 [Yes] [No, let me fix this]
```

Still optional (users know their family better than our algorithm), but it catches obvious typos.

## Testing the Flow: A Manual Checklist

We test the invite-link flow manually (hard to automate with email + token):

- [ ] Invite someone to write a story about a person not yet on the tree
- [ ] Complete signup and tree linking for that person
- [ ] Verify the story auto-links to the correct person
- [ ] Verify the user's PersonId is set after linking
- [ ] Try linking to a person already claimed by another user (should fail cleanly)
- [ ] Try linking when the anchor person has no recorded parents (should place correctly)
- [ ] Verify the story is submitted and appears in the admin queue

This is manual, fragile, and will eventually need a Playwright/Gherkin test suite. For now, we catch regressions through user testing (real family members trying the flow).

## Lessons for User Workflows

1. **Genealogy doesn't have a single schema** — step-siblings, remarriages, adoptions, and cultural naming practices break rigid relationship models. Be permissive in data entry, not prescriptive.

2. **Deduplication needs to be conservative** — fuzzy matching helps, but an 80% threshold means we miss some duplicates. We chose false negatives (user might accidentally create a duplicate) over false positives (falsely accusing "Alice" and "Alicia" of being the same person).

3. **Edge cases hide in user workflows** — linking-to-tree seems straightforward until real families try it. Automated tests pass; manual user testing finds the gotchas.

4. **Errors should be clean** — "Cannot insert duplicate key row" is a database error, not a user-facing message. Every failure path needs a human-readable message so users can fix it.

5. **Accept partial data** — a person without recorded parents is fine; an orphaned relationship is not. Know the difference between incomplete data (acceptable) and corrupt data (critical).

---

**Related:** PR #19 (fuzzy matching), LinkToTreeModal.razor, AuthService.LinkUserToTreeAsync, and manual testing checklist in `docs/Testing/InviteLinkingModelBranchTestingPlan.md`
