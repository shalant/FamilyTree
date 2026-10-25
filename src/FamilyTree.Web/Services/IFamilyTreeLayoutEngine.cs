using FamilyTree.Shared.DTOs;
using FamilyTree.Shared.DTOs.Person;

namespace FamilyTree.Web.Services;

/// <summary>
/// Lays out a family tree for <c>FamilyTreeCanvas</c> and <c>SvgExportService</c>.
/// Implementations (ADR 004): <see cref="FamilyTreeLayoutEngine"/> (archived 2026-10-17,
/// still the default), <see cref="TimelineLayoutEngine"/> (prototype, newest code) and
/// <see cref="BestTimelineLayoutEngine"/> (frozen copy of the best prototype version so far).
/// <c>Layout:Engine</c> in configuration picks the site default: <c>Legacy</c> (default),
/// <c>TimelineBest</c> or <c>Timeline</c>.
/// </summary>
public interface IFamilyTreeLayoutEngine
{
    FamilyTreeLayout ComputeLayout(
        List<PersonDto> people,
        List<CoupleDto> couples,
        Guid? focusPersonId,
        Guid? anchorPersonId = null);
}
