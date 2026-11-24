namespace FamilyTree.Shared.DTOs.Dashboard;

public class DashboardStatsDto
{
    /// <summary>True for the super-user (every family); false for a family Admin (their own family).</summary>
    public bool IsSiteWide { get; set; }
    public int TotalPeople { get; set; }
    public int DeletedCount { get; set; }
    public int TotalRelationships { get; set; }
    public int FamilyCount { get; set; }
    public int UserCount { get; set; }
    public int PendingInvites { get; set; }
    public int TotalAuditEntries { get; set; }
    public int AuditEntriesToday { get; set; }
    public List<AuditEntryDto> RecentAudit { get; set; } = [];
}

public class AuditEntryDto
{
    public Guid? EntityId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public DateTime Timestamp { get; set; }
}
