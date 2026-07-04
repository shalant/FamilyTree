using FamilyTree.Shared;
using FamilyTree.Core.Models;

namespace FamilyTree.Core.Services;

public interface IAdminActivityService
{
    Task<ServiceResponse<List<UserActivity>>> GetRecentActivityAsync(int limit = 200, CancellationToken ct = default);
    Task<ServiceResponse<List<AuditLog>>> GetActivityAuditLogsAsync(Guid? userId, DateOnly date, CancellationToken ct = default);
}
