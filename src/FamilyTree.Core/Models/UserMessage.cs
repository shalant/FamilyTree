namespace FamilyTree.Core.Models;

public class UserMessage
{
    public Guid     Id          { get; set; }
    public string   Type        { get; set; } = "";   // "FeatureRequest" | "ContactAdmin"
    public Guid?    UserId      { get; set; }
    public string   SenderEmail { get; set; } = "";
    public string   Title       { get; set; } = "";
    public string?  Body        { get; set; }
    public string?  Category    { get; set; }
    public string?  Priority    { get; set; }
    public DateTime CreatedAt   { get; set; }
    public DateTime? ReadAt     { get; set; }
}
