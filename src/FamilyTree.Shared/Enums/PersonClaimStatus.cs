namespace FamilyTree.Shared.Enums;

public enum PersonClaimStatus
{
    None,       // user has no linked person node
    Pending,    // user claimed a node; awaiting admin approval
    Approved    // claim confirmed; PersonId is authoritative
}
