namespace FamilyTree.Core.Models;

public class Family
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<Person> People { get; set; } = new List<Person>();
}
