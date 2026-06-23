using FamilyTree.Core.Models;
using FamilyTree.Shared.DTOs.Import;

namespace FamilyTree.Core.Services;

public interface IImportService
{
    bool IsConfigured { get; }
    Task<ImportPreview> ExtractFromTextAsync(string text, CancellationToken ct = default);
    Task<ImportPreview> ExtractFromDocumentAsync(byte[] fileBytes, string fileName, CancellationToken ct = default);
    Task<ImportResult> CommitAsync(ImportPreview preview, CancellationToken ct = default);
    Task<List<ImportBatch>> GetImportBatchesAsync(CancellationToken ct = default);
    Task RollbackBatchAsync(Guid batchId, CancellationToken ct = default);
}
