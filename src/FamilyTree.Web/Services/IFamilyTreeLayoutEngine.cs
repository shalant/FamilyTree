using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Person;

namespace FamilyTree.Web.Services;

/// <summary>
/// Lays out a family tree for <c>FamilyTreeCanvas</c> and <c>SvgExportService</c>.
/// Two implementations (ADR 004): <see cref="FamilyTreeLayoutEngine"/> (archived 2026-10-17,
/// still the default) and <see cref="TimelineLayoutEngine"/> (prototype). <c>Layout:Engine</c>
/// in configuration picks one: <c>Legacy</c> (default) or <c>Timeline</c>.
/// </summary>
public interface IFamilyTreeLayoutEngine
{
    FamilyTreeLayout ComputeLayout(
        List<PersonDto> people,
        List<CoupleDto> couples,
        Guid? focusPersonId,
        Guid? anchorPersonId = null);
}
