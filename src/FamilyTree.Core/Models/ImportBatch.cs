namespace FamilyTree.Core.Models;

public class ImportBatch
{
    public Guid Id { get; set; }
    public string? Note { get; set; }
    public int PersonCount { get; set; }
    public int RelationshipCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RolledBackAt { get; set; }

    // Human-readable debug/audit report assembled at commit: extraction metadata
    // (stop_reason, token usage), per-person match decisions, and the commit outcome.
    // Survives rollback so a problematic import stays diagnosable.
    public string? Report { get; set; }
}
