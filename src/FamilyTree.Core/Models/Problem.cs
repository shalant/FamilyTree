namespace FamilyTree.Core.Models;

/// <summary>
/// Something that went wrong for someone, for the site owner to read (Admin > Problems).
/// No foreign keys: a report must outlive the account or person it mentions.
/// </summary>
public class Problem
{
    public const string Shown = "Shown";           // a red toast someone saw
    public const string Unexpected = "Unexpected"; // the server logged an error

    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string Kind { get; set; } = Shown;
    public Guid? UserId { get; set; }
    public string? UserEmail { get; set; }
    public string? Page { get; set; }
    public string Message { get; set; } = "";
    public string? Source { get; set; }
    public string? Detail { get; set; }
}
