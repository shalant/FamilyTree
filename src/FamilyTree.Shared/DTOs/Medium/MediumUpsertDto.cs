using System.ComponentModel.DataAnnotations;

namespace FamilyTree.Shared.DTOs.Medium;

public class MediumUpsertDto
{
    [Required(ErrorMessage = "Person ID is required.")]
    public Guid PersonId { get; set; }

    [Required(ErrorMessage = "File name is required.")]
    [StringLength(255, ErrorMessage = "File name cannot exceed 255 characters.")]
    public string FileName { get; set; } = "";

    [StringLength(500, ErrorMessage = "Caption cannot exceed 500 characters.")]
    public string? Caption { get; set; }

    [StringLength(100, ErrorMessage = "MIME type cannot exceed 100 characters.")]
    public string? MimeType { get; set; }

    [StringLength(50, ErrorMessage = "Type cannot exceed 50 characters.")]
    public string? Type { get; set; }
}
