namespace FamilyTree.Core.Services;

public interface IGedcomExportService
{
    Task<string> ExportAsync(CancellationToken ct = default);
}
