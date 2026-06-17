using System.ComponentModel.DataAnnotations;

namespace FamilyTree.Shared.DTOs.Story;

public class StoryUpsertDto
{
    public Guid? PersonId { get; set; }

    [StringLength(200, ErrorMessage = "Unlinked person name cannot exceed 200 characters.")]
    public string? UnlinkedPersonName { get; set; }

    [Required(ErrorMessage = "Title is required.")]
    [StringLength(300, ErrorMessage = "Title cannot exceed 300 characters.")]
    public string Title { get; set; } = "";

    [Required(ErrorMessage = "Body is required.")]
    [StringLength(10000, ErrorMessage = "Body cannot exceed 10000 characters.")]
    public string Body { get; set; } = "";

    public Guid? EventId { get; set; }
}
