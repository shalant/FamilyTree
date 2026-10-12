using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Admin;

namespace FamilyTree.Core.Services;

public interface IDataIntegrityService
{
    /// <summary>
    /// Finds every live Relationship whose PersonA or PersonB is soft-deleted (drift that
    /// PersonService.DeleteAsync's cascade should have prevented — see the Elliot Rosenberg
    /// incident, docs/TodoList.md), soft-deletes those relationships to match, and returns
    /// what it fixed. Safe to call with no authenticated user (e.g. from startup) — the fix
    /// is recorded with a null DeletedBy/actor in that case.
    /// </summary>
    Task<ServiceResponse<List<OrphanedRelationshipFixDto>>> FixOrphanedRelationshipsAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Read-only: finds every group of live people caught in a Parent loop (someone recorded
    /// as their own ancestor, R0 in docs/TodoList.md). Bad rows like this crashed the layout
    /// engine before it was made loop-safe, and the write paths now refuse to create one, so
    /// anything this reports predates that. Changes nothing. Only a super-user, or an
    /// unattended caller such as startup (no signed-in user), may run it, because the result
    /// spans every family.
    /// </summary>
    Task<ServiceResponse<List<ParentCycleDto>>> FindParentCyclesAsync(CancellationToken ct = default);
}
