using FamilyTree.Shared;
using FamilyTree.Shared.DTOs.Admin;

namespace FamilyTree.Core.Services;

public interface IDataCheckService
{
    /// <summary>
    /// Read-only: runs <see cref="DataCheckRules"/> over the caller's family and returns what
    /// is probably a data-entry slip (B8, 2026-10-27). Changes nothing. A family Admin sees
    /// their own family; the super-user sees every family, each checked on its own. Anyone
    /// else is refused.
    /// </summary>
    Task<ServiceResponse<List<DataCheckFindingDto>>> GetFindingsAsync(CancellationToken ct = default);
}
