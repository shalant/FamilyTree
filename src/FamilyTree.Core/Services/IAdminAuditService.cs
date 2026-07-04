using FamilyTree.Shared;
using FamilyTree.Core.Models;

namespace FamilyTree.Core.Services;

public interface IAdminAuditService
{
    Task<ServiceResponse<List<AuditLog>>> GetAllAuditLogsAsync(int limit = 500, CancellationToken ct = default);
}
