using System.ComponentModel.DataAnnotations;

namespace FamilyTree.Shared.DTOs.StoryInvite;

public class StoryInviteResponseDto
{
    [Required]
    public string Token { get; set; } = "";

    [StringLength(300, ErrorMessage = "Title cannot exceed 300 characters.")]
    public string? Title { get; set; }

    [Required(ErrorMessage = "Please share your memory before submitting.")]
    [StringLength(10000, ErrorMessage = "Your memory cannot exceed 10000 characters.")]
    public string Body { get; set; } = "";

    [StringLength(200, ErrorMessage = "Name cannot exceed 200 characters.")]
    public string? AuthorName { get; set; }
}
