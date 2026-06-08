namespace FamilyTree.Core.Models;

public partial class Medium
{
    public Guid Id { get; set; }

    public Guid PersonId { get; set; }

    public string Url { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string? Caption { get; set; }
    public string? MimeType { get; set; }
    public string? Type { get; set; }

    public DateTime? CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public byte[]? RowVersion { get; set; }

    public virtual Person Person { get; set; } = null!;
}
