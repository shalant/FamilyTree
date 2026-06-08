namespace FamilyTree.Core.Models;

public class AppUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string? DisplayName { get; set; }
    public Guid? PersonId { get; set; }
    public bool IsSuperUser { get; set; }
    public string? FeatureFlags { get; set; } // JSON: canEdit, isLocked, dailyCrudCap, isDonor
    public DateTime? CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public virtual Person? Person { get; set; }
    public virtual ICollection<UserFamily> UserFamilies { get; set; } = new List<UserFamily>();
    public virtual ICollection<UserInvite> SentInvites { get; set; } = new List<UserInvite>();
    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    public virtual ICollection<UserActivity> Activities { get; set; } = new List<UserActivity>();
}
