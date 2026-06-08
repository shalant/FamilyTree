namespace FamilyTree.Core.Models;

public class UserActivity
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public DateOnly Date { get; set; }
    public int ActionCount { get; set; }

    public virtual AppUser? User { get; set; }
}
