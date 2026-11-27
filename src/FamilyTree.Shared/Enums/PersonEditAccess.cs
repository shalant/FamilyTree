namespace FamilyTree.Shared.Enums;

public enum PersonEditAccess
{
    None,
    /// <summary>Their own record: everything but family links and deleting.</summary>
    OwnDetails,
    Full,
}
