namespace FamilyTree.Core.Models;

public class PasswordResetRequest
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string Token { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? DismissedAt { get; set; }
}
