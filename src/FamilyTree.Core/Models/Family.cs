namespace FamilyTree.Core.Models;

public class Family
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsPublic { get; set; }          // opt-in public discoverability
    public bool RequireApproval { get; set; } = true;  // admin must approve new member claims
    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<Person> People { get; set; } = new List<Person>();
}
