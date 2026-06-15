namespace FamilyTree.Core.Services;

public interface ICurrentUserService
{
    Guid?   UserId   { get; }
    Guid?   FamilyId { get; }
    string? Email    { get; }
}
