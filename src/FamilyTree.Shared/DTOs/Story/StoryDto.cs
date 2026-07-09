namespace FamilyTree.Shared.DTOs.Story;

public class StoryDto
{
    public Guid Id { get; set; }
    public Guid? PersonId { get; set; }
    public string? UnlinkedPersonName { get; set; }
    public Guid? AuthorId { get; set; }
    public string? AuthorDisplayName { get; set; }
    public string? AuthorName { get; set; }
    public Guid? InviteId { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? EventId { get; set; }
    public bool IsApproved { get; set; }
    public bool IsHidden { get; set; }
    public int SortOrder { get; set; }
    public Guid? UserInviteId { get; set; }

    // Set only by StoryInviteService.SubmitResponseAsync — true when the invited
    // email already has an account, so the response page offers "sign in" instead
    // of "create account" (and skips minting a redundant registration invite).
    public bool RecipientHasAccount { get; set; }
    public string? RecipientEmail { get; set; }
}
