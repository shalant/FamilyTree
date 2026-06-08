namespace FamilyTree.Core.Models;

public class UserFamily
{
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }
    public string Role { get; set; } = null!; // Admin / Member / Viewer
    public DateTime JoinedAt { get; set; }

    public virtual AppUser User { get; set; } = null!;
    public virtual Family Family { get; set; } = null!;
}
