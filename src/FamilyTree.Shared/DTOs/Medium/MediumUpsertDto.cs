namespace FamilyTree.Shared.DTOs.Medium;

public class MediumUpsertDto
{
    public Guid PersonId { get; set; }
    public string FileName { get; set; } = "";
    public string? Caption { get; set; }
    public string? MimeType { get; set; }
    public string? Type { get; set; }
}
