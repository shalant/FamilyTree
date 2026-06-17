namespace FamilyTree.Shared.DTOs.StoryInvite;

public class StoryInviteValidationDto
{
    public bool IsValid { get; set; }
    public bool IsExpired { get; set; }
    public bool IsUsed { get; set; }

    public Guid? PersonId { get; set; }
    public string? PersonName { get; set; }
    public string? PersonPhotoUrl { get; set; }
    public int? BirthYear { get; set; }
    public int? DeathYear { get; set; }
    public string? UnlinkedPersonName { get; set; }

    public string? InvitedByDisplayName { get; set; }
    public string? PersonalNote { get; set; }

    public string DisplayName => PersonName ?? UnlinkedPersonName ?? "this family member";
}
