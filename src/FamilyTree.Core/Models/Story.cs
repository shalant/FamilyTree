namespace FamilyTree.Core.Models;

public class Story
{
    public Guid Id { get; set; }

    public Guid? PersonId { get; set; }
    public string? UnlinkedPersonName { get; set; }

    public Guid? AuthorId { get; set; }
    public string? AuthorName { get; set; }

    public Guid? InviteId { get; set; }

    public string Title { get; set; } = null!;
    public string Body { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public Guid? EventId { get; set; }
    public bool IsApproved { get; set; }
    public bool IsHidden { get; set; }
    public int SortOrder { get; set; }

    public virtual Person? Person { get; set; }
    public virtual AppUser? Author { get; set; }
    public virtual StoryInvite? Invite { get; set; }
}
