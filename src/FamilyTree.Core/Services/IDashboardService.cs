using FamilyTree.Shared.DTOs.Dashboard;

namespace FamilyTree.Core.Services;

public interface IDashboardService
{
    Task<DashboardStatsDto> GetDashboardStatsAsync();
}
