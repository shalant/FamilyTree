namespace FamilyTree.Core.Models;

public class UserInvite
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public Guid FamilyId { get; set; }
    public string RoleToGrant { get; set; } = null!;
    public string Token { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual Family Family { get; set; } = null!;
    public virtual AppUser? CreatedByUser { get; set; }
}
