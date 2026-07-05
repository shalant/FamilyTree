# Feature Plan: Post-Signup Tree-Linking Modal

**Status:** Planning  
**Branch:** `feature/invite-linking-modal`  
**Risk Level:** Medium (new user flow, unfinished edge cases acceptable)  
**Date:** 2026-07-04

---

## Overview

When a new user (Willa) completes signup via an invite to tell a story about someone (Bill):
- Willa and Bill may not be on the tree yet
- Guide Willa through a self-service modal to link herself to the tree
- Gracefully handle orphaned stories (both unlinked)
- Don't create fake tree nodes
- Accept some error rate (fixable later via settings)

**Goal:** Reduce friction for contributors while keeping tree data honest.

---

## User Flows

### **Flow 1: Willa IS on the tree**
```
Story submitted for Bill (unlinked)
↓
Signup complete, redirect to /link-to-tree modal
↓
Q1: "Are you on this tree?" → YES
↓
Q2: "Who are you?" [Picker of tree nodes]
↓
Willa confirms: "That's me"
↓
Result:
  - Willa.PersonId = [selected node]
  - Bill.PersonId = null (story orphaned)
  - User sees: "You're linked! Bill's story is waiting for Bill to be added."
  - ✅ Willa on tree, can navigate, see tree
```

---

### **Flow 2: Willa NOT on tree, but knows someone**
```
Story submitted for Bill (unlinked)
↓
Signup complete, redirect to modal
↓
Q1: "Are you on this tree?" → NO
↓
Q2: "Do you know someone on the tree?" → YES
↓
Q3: "Who?" [Picker: Ray, Doug, etc.]
↓
*[IF SELECTED NAME SHARES LAST NAME WITH WILLA]*
  Branch: "We noticed you and Bill both have 'Small' 
           as a last name. Is Bill related to Ray?"
    ○ Yes → "How are they related?" (sibling, cousin, etc.)
    ○ No → skip branch
    ○ Not sure → skip branch

↓
Q4: "How are you related to [Doug]?" 
    (sibling, child, cousin, spouse, other)
↓
Result:
  - Willa.PersonId = [auto-created node]
  - Willa linked to Doug as [relationship]
  - Bill.PersonId = null (or linked if "Yes" to last-name branch)
  - Story now linked to Bill if Bill exists, else orphaned
```

---

### **Flow 3: Willa NOT on tree, Bill IS on tree**
```
Story submitted for Bill (exists on tree)
↓
Signup complete, redirect to modal
↓
Q1: "Are you on this tree?" → NO
↓
Q2: "Do you know someone on the tree?" → NO
↓
Q3: "But...is the person you told a story about 
      (Bill) on the tree?" → YES
↓
Q4: "Which Bill?" [Picker of Bills on tree, with dates/context]
↓
Result:
  - Willa.PersonId = null (unlinked contributor)
  - Bill.PersonId = [confirmed existing]
  - Story linked to Bill ✅
  - Willa in "Contributors" sidebar
  - User sees: "Your story about Bill is linked!"
```

---

### **Flow 4: Willa NOT on tree, Bill NOT on tree**
```
Story submitted for Bill (unlinked)
↓
Signup complete, redirect to modal
↓
Q1: "Are you on this tree?" → NO
↓
Q2: "Do you know someone on the tree?" → NO
↓
Q3: "Is Bill on the tree?" → NO
↓
Result:
  - Willa.PersonId = null
  - Bill.PersonId = null
  - Story orphaned (waiting for Bill to be added)
  - User sees: "Story saved. Come back when someone adds Bill."
  - Can revisit via settings
```

---

### **Flow 5: Willa NOT on tree, unsure about connections**
```
Any flow → User clicks "I'm not sure / I'll figure this out later"
↓
Result:
  - Willa.PersonId = null
  - Story status: "pending link"
  - Dismiss modal
  - Can revisit via settings → "Complete your profile"
  - No data lost
```

---

## Components & Architecture

### **New Component: `LinkToTreeModal.razor`**
- Stateful component (tracks current question, responses)
- Sequential Q&A flow (reusable pattern for future similar flows)
- Conditional rendering based on responses
- Submit handler that creates/updates Willa's PersonId

**Props:**
- `StoryInviteToken` (string) — to fetch story details (Bill)
- `OnLinkingComplete` (EventCallback) — dismiss modal and navigate

**Internal State:**
```csharp
private int _currentQuestion = 0;
private Dictionary<string, object> _responses = new();  // Q1→answer, etc.
private bool _isLoadingPicker = false;
private List<PersonDto> _treeNodes = [];
private PersonDto? _selectedPerson = null;
```

---

### **Service Changes: `IAuthService` / `AuthService`**
Add method:
```csharp
Task<ServiceResponse> LinkUserToTreeAsync(
  Guid userId, 
  Guid? personId, 
  Guid? spouseId,  // if applicable
  string? relationshipType)
```

Behavior:
- If `personId` is null → create new Person node for the user
- If `personId` exists → link user to existing person
- Create Relationship if `relationshipType` specified

---

### **Database Changes: None (Initial)**
- Use existing `AppUser.PersonId` field
- Use existing `Person`, `Relationship` entities
- No schema migrations needed

---

## Edge Cases Handled (MVP)

| Case | Handling |
|------|----------|
| **Multiple Bills on tree** | Picker shows Bill + birth year/spouse to disambiguate |
| **Common last name** | Only auto-offer branch if last name is rare (< 100 in typical tree) |
| **Both Willa & Bill unlinked** | Story orphaned, "come back later" message |
| **User skips modal** | Don't force; "I'm not sure" escape hatch |
| **User gets it wrong** | Settings → "Update your tree position" → re-run modal |
| **Willa is duplicate** | Handled separately later (merge tool) |
| **Cross-cultural names** | Gate on "name appears in tree" heuristic, not assumptions |

---

## Edge Cases NOT Handled (Future)

- ❌ Automatic duplicate detection (if Willa somehow matches existing person)
- ❌ Multi-family membership (Willa joining multiple family trees)
- ❌ Verification of Willa's claims (Doug confirming)
- ❌ Analytics on modal completion rate / abandonment
- ❌ Localization (currently English-only)

---

## Testing Strategy

### **Unit Tests**
- `LinkToTreeModal`: Component state transitions (Q1→Q2→Q3, etc.)
- `AuthService.LinkUserToTreeAsync`: Creates/links person correctly

### **Integration Tests**
- Full flow: Signup → Modal → Redirect to home
- Verify Willa appears on tree vs. sidebar based on PersonId

### **Manual Testing (User's Responsibility)**
1. **Happy path:** Willa on tree + Bill on tree → both linked ✅
2. **Orphan path:** Neither on tree → story orphaned, message clear
3. **Auto-discovery:** Willa + Bill share last name → branch fires correctly
4. **Escape hatch:** "I'm not sure" → modal closes, no PersonId set
5. **Update flow:** Willa in settings → "Complete profile" → re-run modal ✅

---

## Implementation Checklist

- [ ] Create `LinkToTreeModal.razor` component
- [ ] Add `LinkUserToTreeAsync` to `AuthService`
- [ ] Update signup flow to redirect to modal
- [ ] Add "Update tree position" button in user settings (future: refactor to use same modal)
- [ ] Add "Common surnames" list (hardcoded initially)
- [ ] Test all 5 flows
- [ ] Verify redirect chain: signup → modal → home
- [ ] Check tree rendering (Willa appears vs. sidebar)

---

## Rollback Plan

If this feature is rejected:
1. Remove `LinkToTreeModal.razor`
2. Remove redirect in signup flow (revert to direct home navigation)
3. Users who linked themselves: PersonId stays set (no data loss)
4. Users who skipped: PersonId = null (same as before)

---

## Future Extensions

- "Confirm this person" — Admin oversight without full approval
- "Merge candidates" — "We found someone who might be you"
- "Update tree position" — Self-serve correction
- Multi-family support — Willa joins multiple family trees
- Analytics — Track modal completion, where users bail out

---

## Notes & Risks

**Risk: Orphaned Stories**
- Stories about Bill (who doesn't exist yet) will float unlinked
- Acceptable for MVP; can query orphaned stories later
- Plan: "Come back when Bill is added" → eventual completion

**Risk: Wrong Links**
- Willa might say "I'm Doug's cousin" incorrectly
- Mitigated by confirmation screen ("Here's how you'll appear")
- Future: "Update tree position" self-serve fix

**Risk: Modal Abandonment**
- Users might close modal and never link themselves
- Mitigated by "I'm not sure" button and settings shortcut
- Acceptable for MVP

**Nice-to-Have: Last-Name Branch**
- Requires hardcoded list of common surnames
- Risk of false positives/negatives
- Can disable if problematic during testing

---

## Scope Boundary: Story Invites Require an Existing Person (decided 2026-07-04)

**Decision:** a story invite can only be sent about someone **already on the tree**.
`StoryInviteDialog.razor` no longer has a "they're not in the tree yet" free-text
option — the sender must search for and select an existing person, or pick
"Add someone new…" (which opens `/people/add?name=<typed text>` in a new tab, the same
pattern already used by the Admin unlinked-stories linking queue) to create that person
first, then come back and select them. `StoryInviteCreateDto.PersonId` is non-nullable
and `StoryInviteService.CreateInviteAsync` rejects `Guid.Empty` outright.

This supersedes the original "one-hop self-service linking" version of this decision
(also written 2026-07-04, superseded same day): rather than letting the *invited*
person's linking wizard resolve one hop to the tree on their own, the *inviter* — who
already understands the family structure — now resolves that hop themselves, before the
invite ever goes out. By the time the invited person (e.g. Willa) completes her story
and registers, `Story.PersonId` is already set to an existing Person (e.g. Bill), so the
linking modal never needs its "subject-first" flow (ask about Bill, maybe create him) at
all — she only ever answers "how are you related to Bill," a much simpler, better-tested
path.

**Why:** the layout-engine work this session (see `Commentary/UnlinkedInviteProblem.md`
and the `FamilyTreeLayoutEngineTests` regression suite) showed that even a *single* hop
outside the normal parent→child recursive structure (an explicit sibling relationship
with no shared parent) touched connected-component detection, generational depth,
birth-year inference, and connector rendering — and took several rounds to get right.
Letting a stranger who doesn't know the tree drive that same fragile machinery through a
self-service wizard was judged not worth it, especially given the product's low
tolerance for rough edges in real family members' hands (see the
`feedback_quality_bar_arborkin` memory note from this same conversation).

**What this means for existing code:**
- `LinkToTreeModal`'s subject-first flow (`Stage.IsSubjectOnTree` through
  `Stage.SubjectRelationToConnection`, `AuthService.CreateUnlinkedPersonAsync`) is
  **not removed** — it's still exercised by `StoryPendingLinkingSubjectTests` and is
  harmless, tested code — but it should now be effectively unreachable via the normal
  invite path, since `Story.PersonId` is always set at invite-creation time going
  forward. It only matters for **pre-existing** unlinked invites/stories created before
  this change.
- The layout-engine fixes (sibling component detection, depth propagation, birth-year
  inference through siblings/spouses, the dashed `SiblingLink` connector) remain fully
  necessary — an admin can still create an orphan-sibling structure directly via
  "Add Person," and the tree needs to render that correctly regardless of how it got
  there.

**If this needs to change later:** watch for senders finding "Add someone new…" too
much friction for a quick invite. If that happens, the original one-hop wizard version
of this decision is still intact in the code and could be re-enabled by relaxing the
`StoryInviteCreateDto.PersonId` requirement again.

---

## Success Metrics (for future, not MVP)

- % of invited users complete modal (goal: >70%)
- % of linked users who later correct themselves (goal: <5%)
- Average time to complete modal (goal: <2 min)
- Orphaned stories resolved within X days (goal: <7 days)

