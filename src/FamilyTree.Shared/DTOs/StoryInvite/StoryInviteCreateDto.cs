using System.ComponentModel.DataAnnotations;

namespace FamilyTree.Shared.DTOs.StoryInvite;

public class StoryInviteCreateDto
{
    public Guid? PersonId { get; set; }

    [StringLength(200, ErrorMessage = "Person name cannot exceed 200 characters.")]
    public string? UnlinkedPersonName { get; set; }

    [Required(ErrorMessage = "Recipient email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256)]
    public string InvitedEmail { get; set; } = "";

    [StringLength(1000, ErrorMessage = "Personal note cannot exceed 1000 characters.")]
    public string? PersonalNote { get; set; }
}
