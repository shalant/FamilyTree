using System.Collections.Concurrent;
using System.Net;
using FamilyTree.Core.Services;
using Microsoft.AspNetCore.Components.Forms;

namespace FamilyTree.Web.Services;

// A failed upload used to show the raw .NET exception ("NotReadableError: The requested file
// could not be read...", found on a phone 2026-11-26), and nobody but that person ever knew.
// The page now shows UploadFailure.MessageFor and tells the admin through this.
public static class UploadFailure
{
    public static string MessageFor(long fileSize, long maxBytes, string noun = "photo") =>
        fileSize > maxBytes
            ? $"That {noun} is too big. The limit is {maxBytes / (1024 * 1024)} MB."
            : $"That {noun} couldn't be opened. Save it to your device first, then try again, or pick a different one.";
}

public interface IUploadFailureReporter
{
    /// <summary>Logs the failure and emails the admin. Never throws.</summary>
    Task ReportAsync(IBrowserFile file, long maxBytes, Exception error);
}

// One email per person per window: someone retrying a bad file shouldn't fill the inbox.
public sealed class UploadAlertThrottle
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, DateTime> _lastSent = new();

    public bool TryAcquire(string key, DateTime utcNow)
    {
        if (_lastSent.TryGetValue(key, out var last) && utcNow - last < Window) return false;
        _lastSent[key] = utcNow;
        return true;
    }
}

public class UploadFailureReporter(
    IEmailSender emailSender,
    IConfiguration configuration,
    ICurrentUserService currentUser,
    UploadAlertThrottle throttle,
    ILogger<UploadFailureReporter> logger) : IUploadFailureReporter
{
    public async Task ReportAsync(IBrowserFile file, long maxBytes, Exception error)
    {
        try
        {
            logger.LogError(error, "Upload failed: {ContentType}, {Size} bytes, user {UserId}",
                file.ContentType, file.Size, currentUser.UserId);

            // Too big is an expected refusal, and the person is told why.
            if (file.Size > maxBytes) return;

            var to = configuration["Ops:AlertEmail"] ?? configuration["SuperUser:Email"];
            if (string.IsNullOrWhiteSpace(to)) return;
            if (!throttle.TryAcquire(currentUser.UserId?.ToString() ?? "unknown", DateTime.UtcNow)) return;

            var html = $"""
                <p><strong>A file upload failed on ArborKin.</strong></p>
                <ul>
                  <li>Who: {WebUtility.HtmlEncode(currentUser.Email ?? "unknown")}</li>
                  <li>File: {WebUtility.HtmlEncode(file.Name)} ({WebUtility.HtmlEncode(file.ContentType)}, {file.Size:N0} bytes)</li>
                  <li>Time (UTC): {DateTime.UtcNow:u}</li>
                  <li>Error: {WebUtility.HtmlEncode($"{error.GetType().Name}: {error.Message}")}</li>
                </ul>
                <p>Further failures from this person in the next {UploadAlertThrottle.Window.TotalMinutes:0} minutes are logged but not emailed.</p>
                """;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await emailSender.SendAsync(to, "ArborKin: a file upload failed", html, cts.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to report an upload failure");
        }
    }
}
