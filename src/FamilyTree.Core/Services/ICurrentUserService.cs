namespace FamilyTree.Core.Services;

public interface ICurrentUserService
{
    Guid?   UserId      { get; }
    Guid?   FamilyId    { get; }
    string? Email       { get; }

    /// <summary>
    /// True only for a genuine super-user (the "SuperUser" role claim). Family-scoping
    /// checks must gate the "see everything, including FamilyId == null records" bypass
    /// on THIS, not on "FamilyId is null" — a regular user whose UserFamily assignment
    /// is missing also has a null FamilyId, and treating that the same as a super-user
    /// would let them see every family's data instead of none.
    /// </summary>
    bool    IsSuperUser { get; }
}
