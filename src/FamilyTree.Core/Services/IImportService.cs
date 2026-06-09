using FamilyTree.Shared.DTOs.Import;

namespace FamilyTree.Core.Services;

public interface IImportService
{
    bool IsConfigured { get; }
    Task<ImportPreview> ExtractFromTextAsync(string text, CancellationToken ct = default);
    Task<ImportResult> CommitAsync(ImportPreview preview, CancellationToken ct = default);
}
