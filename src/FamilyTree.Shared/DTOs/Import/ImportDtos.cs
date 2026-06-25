namespace FamilyTree.Shared.DTOs.Import;

public class ImportPersonDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? MiddleName { get; set; }
    public string? MaidenName { get; set; }
    public string[]? Aliases { get; set; }
    public string? BirthDate { get; set; }
    public string? DeathDate { get; set; }
    public bool IsDeceased { get; set; }
    public string Gender { get; set; } = "Unknown";
    public string? Notes { get; set; }
    public bool ReferenceOnly { get; set; }
    public bool Selected { get; set; } = true;

    // Entity-resolution: when set, this imported person is LINKED to an existing
    // tree member (its Guid) rather than created anew. Null = create new (default).
    public Guid? MatchedExistingId { get; set; }

    // When linking, the canonical field names the user chose to merge from this
    // import into the existing record (e.g. ["DeathDate", "BiographyNotes"]). Empty =
    // link as-is (existing record untouched). See ImportMergeFields for valid keys.
    public List<string>? MergeFieldNames { get; set; }

    // Suggested existing-person matches, populated by the matcher before preview.
    // Not persisted; purely drives the link/create affordance in the UI.
    public List<MatchCandidate>? Candidates { get; set; }
}

// Canonical field keys the reconcile dialog can merge from an import into an existing
// person. These are the fields the extraction actually captures.
public static class ImportMergeFields
{
    public const string BirthDate      = "BirthDate";
    public const string DeathDate      = "DeathDate";
    public const string MiddleName     = "MiddleName";
    public const string MaidenName     = "MaidenName";
    public const string Gender         = "Gender";
    public const string BiographyNotes = "BiographyNotes";
}

public class MatchCandidate
{
    public Guid PersonId { get; set; }
    public string DisplayName { get; set; } = "";
    public int? BirthYear { get; set; }
    public double Score { get; set; }   // 0..1 confidence, best-first
}

public class ImportRelationshipDto
{
    public string Type { get; set; } = "";  // "Parent" | "Spouse" | "Sibling"
    public int PersonAId { get; set; }
    public int PersonBId { get; set; }
    public bool IsFormer { get; set; }
    public string? MarriageDate { get; set; }
}

public class ImportPreview
{
    public List<ImportPersonDto> People { get; set; } = new();
    public List<ImportRelationshipDto> Relationships { get; set; } = new();
    public string? Warnings { get; set; }
    public ImportDiagnostics? Diagnostics { get; set; }
}

// Diagnostics captured during extraction + matching, surfaced in the import report.
public class ImportDiagnostics
{
    public string? Model { get; set; }
    public string? StopReason { get; set; }   // "end_turn" = complete; "max_tokens" = truncated
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int RawResponseLength { get; set; }
    public bool JsonRepairApplied { get; set; }
    public int MatchPoolSize { get; set; }    // # existing people compared against
    public int SourcePages { get; set; }      // 0 = paste/unknown
}

public record ImportResult(int PeopleCreated, int RelationshipsCreated, int LinkedToExisting = 0, string? Error = null);

// Live progress reported during Claude extraction — used by ImportFormPanel to
// narrate the actual pipeline stages instead of showing a blank spinner.
public record ImportProgress(string Stage, int PeopleFoundSoFar = 0, string? Detail = null);
