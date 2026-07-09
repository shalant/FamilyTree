using FamilyTree.Shared.DTOs.Person;

namespace FamilyTree.Web.Modules.Pages;

// Extracted out of Home.razor so the check is unit-testable without JS interop/DialogService
// setup. A user's claimed Person can go stale in exactly one way: AppUser.PersonId stays set
// after the Person it points at is soft-deleted (found 2026-07-08 — an admin deleting the
// wrong row, or cleaning up a duplicate, left a dangling PersonId). PersonId.HasValue alone
// can't tell "never linked" apart from "was linked, now dangling" — both need the same
// treatment (route into the recovery-linking flow instead of silently falling back to an
// arbitrary stranger's tree), so callers should use IsResolved rather than checking HasValue.
public static class LinkedPersonResolver
{
    public static bool IsResolved(Guid? claimedPersonId, IEnumerable<PersonDto> people) =>
        claimedPersonId.HasValue && people.Any(p => p.Id == claimedPersonId.Value);
}
