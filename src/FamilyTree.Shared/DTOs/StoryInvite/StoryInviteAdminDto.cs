namespace FamilyTree.Shared.DTOs.StoryInvite;

public class StoryInviteAdminDto
{
    public Guid Id { get; set; }
    public string InvitedEmail { get; set; } = "";
    public Guid? PersonId { get; set; }
    public string? PersonName { get; set; }
    public string? UnlinkedPersonName { get; set; }
    public string? InvitedByDisplayName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public bool SendFailed { get; set; }
    public string? SendError { get; set; }
}
