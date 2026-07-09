using System.ComponentModel.DataAnnotations;

namespace FamilyTree.Shared.DTOs.StoryInvite;

public class StoryInviteCreateDto
{
    // Deliberately non-nullable: a story invite can only be sent about someone already
    // on the tree. If the subject isn't there yet, the sender adds them first via
    // "Add Person" — see docs/FEATURE_PLAN_INVITE_LINKING_MODAL.md, "Scope Boundary:
    // One-Hop Self-Service Linking." (Guid.Empty is rejected in StoryInviteService,
    // not here — [Required] is a no-op on a non-nullable value type.)
    public Guid PersonId { get; set; }

    [Required(ErrorMessage = "Recipient email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256)]
    public string InvitedEmail { get; set; } = "";

    [StringLength(1000, ErrorMessage = "Personal note cannot exceed 1000 characters.")]
    public string? PersonalNote { get; set; }
}
