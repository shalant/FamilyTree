namespace FamilyTree.Core.Services;

public interface IAuditLogService
{
    Task LogAsync(string action, string entityType, Guid? entityId,
        string? oldValue = null, string? newValue = null,
        Guid? userId = null, string? ipAddress = null,
        CancellationToken ct = default);
}
