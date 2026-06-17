namespace FamilyTree.Core.Models;

public class StoryInvite
{
    public Guid Id { get; set; }

    public string Token { get; set; } = null!;

    public Guid? PersonId { get; set; }
    public string? UnlinkedPersonName { get; set; }

    public string InvitedEmail { get; set; } = null!;
    public Guid InvitedByUserId { get; set; }
    public string? PersonalNote { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public bool SendFailed { get; set; }
    public string? SendError { get; set; }

    public virtual Person? Person { get; set; }
    public virtual AppUser? InvitedByUser { get; set; }
}
