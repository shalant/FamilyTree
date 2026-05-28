namespace FamilyTree.Api.Services;

/// <summary>
/// Abstraction for blob storage (Azure Blob Storage, local file system, S3, etc.).
/// Implementations handle upload/download/delete of media files.
/// </summary>
public interface IBlobStorageService
{
    /// <summary>Upload a file and return its URI.</summary>
    Task<string> UploadAsync(Stream fileStream, string fileName, string? mimeType = null, CancellationToken ct = default);

    /// <summary>Download a file by its URI or identifier.</summary>
    Task<Stream> DownloadAsync(string fileIdentifier, CancellationToken ct = default);

    /// <summary>Delete a file by its URI or identifier.</summary>
    Task<bool> DeleteAsync(string fileIdentifier, CancellationToken ct = default);

    /// <summary>Check if a file exists.</summary>
    Task<bool> ExistsAsync(string fileIdentifier, CancellationToken ct = default);
}
