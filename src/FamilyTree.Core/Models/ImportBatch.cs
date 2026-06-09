namespace FamilyTree.Core.Models;

public class ImportBatch
{
    public Guid Id { get; set; }
    public string? Note { get; set; }
    public int PersonCount { get; set; }
    public int RelationshipCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RolledBackAt { get; set; }
}
