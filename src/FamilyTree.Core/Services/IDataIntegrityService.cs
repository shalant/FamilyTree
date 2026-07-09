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
}
