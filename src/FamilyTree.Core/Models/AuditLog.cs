namespace FamilyTree.Core.Models;

public class AuditLog
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = null!;     // Create / Update / Delete / Restore / Login / RoleChange
    public string EntityType { get; set; } = null!; // Person / Relationship / Medium / User
    public Guid? EntityId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? IpAddress { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    public virtual AppUser? User { get; set; }
}
