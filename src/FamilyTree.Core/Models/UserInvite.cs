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

    /// <summary>
    /// The person on the tree this invite is for (Admin > Users, "Who is this?"). When set, the
    /// account made from the invite is linked to them at sign-up and the "Who are you?" step is
    /// skipped. Null for story invites and for an invite sent without naming anyone.
    /// </summary>
    public Guid? PersonId { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual Family Family { get; set; } = null!;
    public virtual AppUser? CreatedByUser { get; set; }
}
