using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FamilyTree.Core.Services;

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly IConfiguration _config;
    private readonly ILogger<BlobStorageService> _logger;

    public BlobStorageService(
        BlobServiceClient blobServiceClient,
        IConfiguration config,
        ILogger<BlobStorageService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _config = config;
        _logger = logger;
    }

    private string ContainerName => _config["AzureStorage:ContainerName"] ?? "media";

    public async Task<string> UploadAsync(
        Stream fileStream,
        string fileName,
        string? mimeType = null,
        CancellationToken ct = default)
    {
        try
        {
            //await fileStream.FlushAsync(ct);
            //fileStream.Position = 0;

            var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
            var blobClient = containerClient.GetBlobClient(fileName);

            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = mimeType ?? "application/octet-stream" }
            };

            //await blobClient.UploadAsync(fileStream, overwrite: true, options: uploadOptions, cancellationToken: ct);
            await blobClient.UploadAsync(fileStream, overwrite: true, cancellationToken: ct);

            _logger.LogInformation("Uploaded blob {FileName} to {Container}", fileName, ContainerName);
            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading blob {FileName}", fileName);
            throw;
        }
    }

    public async Task<Stream> DownloadAsync(string fileIdentifier, CancellationToken ct = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
            var blobName = ExtractBlobName(fileIdentifier);
            var blobClient = containerClient.GetBlobClient(blobName);

            var download = await blobClient.DownloadAsync(cancellationToken: ct);
            _logger.LogInformation("Downloaded blob {BlobName}", blobName);
            return download.Value.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading blob {FileIdentifier}", fileIdentifier);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string fileIdentifier, CancellationToken ct = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
            var blobName = ExtractBlobName(fileIdentifier);
            var blobClient = containerClient.GetBlobClient(blobName);

            var result = await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
            _logger.LogInformation("Deleted blob {BlobName}, existed: {Existed}", blobName, result.Value);
            return result.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting blob {FileIdentifier}", fileIdentifier);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string fileIdentifier, CancellationToken ct = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
            var blobName = ExtractBlobName(fileIdentifier);
            var blobClient = containerClient.GetBlobClient(blobName);

            var exists = await blobClient.ExistsAsync(cancellationToken: ct);
            return exists.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking blob existence {FileIdentifier}", fileIdentifier);
            return false;
        }
    }

    private static string ExtractBlobName(string fileIdentifier)
    {
        // If it's a full URI, extract just the blob name
        if (fileIdentifier.StartsWith("http"))
        {
            var uri = new Uri(fileIdentifier);
            return uri.AbsolutePath.TrimStart('/').Split('/').Last();
        }
        return fileIdentifier;
    }
}