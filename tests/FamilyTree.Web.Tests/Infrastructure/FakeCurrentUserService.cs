using FamilyTree.Core.Services;

public sealed class FakeCurrentUserService : ICurrentUserService
{
    public Guid?   UserId      { get; set; }
    public Guid?   FamilyId    { get; set; }
    public string? Email       { get; set; }
    public bool    IsSuperUser { get; set; }
    public bool    IsAdmin     { get; set; }
}
