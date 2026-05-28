namespace FamilyTree.Shared.Enums;

/// <summary>
/// Directional: PersonA → PersonB.
/// Parent  = PersonA is a parent of PersonB.
/// Spouse  = treat as bidirectional (one record per pair).
/// Sibling = one record per pair.
/// Adopted = PersonA is the adoptive parent of PersonB.
/// </summary>
public enum RelationshipType
{
    Parent,
    Spouse,
    Sibling,
    Adopted
}
