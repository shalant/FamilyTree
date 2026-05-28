namespace FamilyTree.Api.Services;

/// <summary>
/// Stub implementation of blob storage. Replace with Azure Blob Storage or other backend.
/// </summary>
public class BlobStorageService : IBlobStorageService
{
    private readonly ILogger<BlobStorageService> _logger;

    public BlobStorageService(ILogger<BlobStorageService> logger)
    {
        _logger = logger;
    }

    public async Task<string> UploadAsync(
        Stream fileStream,
        string fileName,
        string? mimeType = null,
        CancellationToken ct = default)
    {
        // TODO: Implement blob storage upload
        // For now, log and return a placeholder URI
        _logger.LogInformation("Upload stub called for {FileName}", fileName);
        await Task.CompletedTask;
        return $"blob://placeholder/{fileName}";
    }

    public async Task<Stream> DownloadAsync(string fileIdentifier, CancellationToken ct = default)
    {
        // TODO: Implement blob storage download
        _logger.LogInformation("Download stub called for {FileIdentifier}", fileIdentifier);
        await Task.CompletedTask;
        return new MemoryStream();
    }

    public async Task<bool> DeleteAsync(string fileIdentifier, CancellationToken ct = default)
    {
        // TODO: Implement blob storage delete
        _logger.LogInformation("Delete stub called for {FileIdentifier}", fileIdentifier);
        await Task.CompletedTask;
        return true;
    }

    public async Task<bool> ExistsAsync(string fileIdentifier, CancellationToken ct = default)
    {
        // TODO: Implement blob storage exists check
        _logger.LogInformation("Exists stub called for {FileIdentifier}", fileIdentifier);
        await Task.CompletedTask;
        return true;
    }
}
