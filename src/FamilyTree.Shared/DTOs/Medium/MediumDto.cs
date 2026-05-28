namespace FamilyTree.Shared.DTOs.Medium;

public class MediumDto
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public string Url { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? Caption { get; set; }
    public string? MimeType { get; set; }
    public string? Type { get; set; }
    public DateTime? CreatedAt { get; set; }
    public byte[]? RowVersion { get; set; }
}
