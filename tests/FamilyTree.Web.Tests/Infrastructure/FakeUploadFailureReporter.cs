using FamilyTree.Web.Services;
using Microsoft.AspNetCore.Components.Forms;

public sealed class FakeUploadFailureReporter : IUploadFailureReporter
{
    public List<(IBrowserFile File, Exception Error)> Reports { get; } = [];

    public Task ReportAsync(IBrowserFile file, long maxBytes, Exception error)
    {
        Reports.Add((file, error));
        return Task.CompletedTask;
    }
}

// What a phone browser hands over when it can't read the picked file (2026-11-26).
public sealed class UnreadableFile(long size = 1024) : IBrowserFile
{
    public string Name => "1000028717.jpg";
    public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
    public long Size => size;
    public string ContentType => "image/jpeg";

    public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        => throw new IOException("NotReadableError: The requested file could not be read");
}

public sealed class ReadableFile : IBrowserFile
{
    public string Name => "new.jpg";
    public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
    public long Size => 3;
    public string ContentType => "image/jpeg";

    public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        => new MemoryStream([1, 2, 3]);
}
