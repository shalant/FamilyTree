namespace FamilyTree.Shared.DTOs.Admin;

// One "probably wrong, please check" finding from DataCheckService (B8, 2026-10-27).
// Read-only report: nothing is changed.
public class DataCheckFindingDto
{
    // DataCheckRule's name, e.g. "Marriage"; the tab groups by it.
    public string Rule { get; set; } = "";

    // True when the data can't be right (the server refuses it today; the row predates the
    // rule); false when it's only unlikely.
    public bool IsImpossible { get; set; }

    public string Message { get; set; } = "";
    public List<DataCheckPersonDto> People { get; set; } = [];

    // The family the people belong to. Only the super-user sees more than one.
    public Guid? FamilyId { get; set; }
    public string FamilyName { get; set; } = "";
}

public class DataCheckPersonDto
{
    public Guid PersonId { get; set; }
    public string Name { get; set; } = "";
}
