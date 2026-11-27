using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace FamilyTree.Web.Services;

// Why this exists: a member's refused save on 2026-11-26 was recorded nowhere. Two sources feed
// one table: red toasts (what someone saw) and the server's error log (what actually broke,
// including crashes that show no toast). Only the second sends email.
public sealed record PendingProblem(Problem Problem, bool Email);

/// <summary>Takes reports without blocking or failing the caller; drops the oldest if flooded.</summary>
public sealed class ProblemQueue
{
    private readonly Channel<PendingProblem> _channel = Channel.CreateBounded<PendingProblem>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });

    public ChannelReader<PendingProblem> Reader => _channel.Reader;

    public void Add(Problem problem, bool email = false)
    {
        problem.Message = Cut(ProblemText.Scrub(problem.Message), 1000)!;
        problem.Detail = Cut(ProblemText.Scrub(problem.Detail), 4000);
        problem.Page = Cut(problem.Page, 300);
        problem.Source = Cut(problem.Source, 200);
        _channel.Writer.TryWrite(new PendingProblem(problem, email));
    }

    private static string? Cut(string? text, int max) =>
        text is null || text.Length <= max ? text : text[..max];
}

/// <summary>Keeps sign-in, invite and story tokens out of the table and the email.</summary>
public static partial class ProblemText
{
    [GeneratedRegex(@"((?:story/respond|media/invite)/|(?:invite|token|code)=)[^/?&#\s""']+", RegexOptions.IgnoreCase)]
    private static partial Regex Token();

    public static string? Scrub(string? text) => text is null ? null : Token().Replace(text, "$1…");

    public static string Page(string uri) => Scrub(new Uri(uri).AbsolutePath)!;
}

public interface IToastProblemRecorder
{
    void Record(string message);
}

public sealed class ToastProblemRecorder(
    ProblemQueue queue, NavigationManager navigation, ICurrentUserService currentUser) : IToastProblemRecorder
{
    public void Record(string message)
    {
        try
        {
            queue.Add(new Problem
            {
                Kind = Problem.Shown,
                Message = message,
                Page = ProblemText.Page(navigation.Uri),
                UserId = currentUser.UserId,
                UserEmail = currentUser.Email,
            });
        }
        catch
        {
            // Recording must never break the page that is already reporting a failure.
        }
    }
}

public sealed class ProblemLoggerProvider(ProblemQueue queue) : ILoggerProvider
{
    /// <summary>Set while a report is being written, so a failing write can't report itself.</summary>
    internal static readonly AsyncLocal<bool> Writing = new();

    public ILogger CreateLogger(string categoryName) => new Logger(queue, categoryName);

    public void Dispose() { }

    private sealed class Logger(ProblemQueue queue, string category) : ILogger
    {
        // EF logs the same failure the service then logs with context.
        private readonly bool _ignored = category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal);
        // Upload failures already send their own, more detailed, email.
        private readonly bool _email = category != typeof(UploadFailureReporter).FullName;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || _ignored || Writing.Value) return;
            // A phone locking or a tab closing mid-call is not a fault.
            if (exception is OperationCanceledException or JSDisconnectedException) return;

            queue.Add(new Problem
            {
                Kind = Problem.Unexpected,
                Source = category,
                Message = formatter(state, exception),
                Detail = exception?.ToString(),
            }, _email);
        }
    }
}

public sealed class ProblemWriter(
    ProblemQueue queue,
    IDbContextFactory<AppDbContext> dbFactory,
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<ProblemWriter> logger) : BackgroundService
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(90);

    // One email per window for everything: during an outage the list has the detail.
    private readonly UploadAlertThrottle _throttle = new();
    private DateTime _lastPurge;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var pending in queue.Reader.ReadAllAsync(stoppingToken))
            await ProcessAsync(pending, stoppingToken);
    }

    /// <summary>Stores one report, and emails it if asked. Never throws.</summary>
    public async Task ProcessAsync(PendingProblem pending, CancellationToken ct = default)
    {
        ProblemLoggerProvider.Writing.Value = true;
        try
        {
            // Each step on its own: when the database is the problem, the email still goes.
            try
            {
                await using var ctx = await dbFactory.CreateDbContextAsync(ct);
                if (DateTime.UtcNow - _lastPurge > TimeSpan.FromDays(1))
                {
                    var cutoff = DateTime.UtcNow - KeepFor;
                    ctx.Problems.RemoveRange(ctx.Problems.Where(p => p.OccurredAt < cutoff));
                    _lastPurge = DateTime.UtcNow;
                }
                ctx.Problems.Add(pending.Problem);
                await ctx.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not store a problem report");
            }

            if (pending.Email) await EmailAsync(pending.Problem);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not email a problem report");
        }
        finally
        {
            ProblemLoggerProvider.Writing.Value = false;
        }
    }

    private async Task EmailAsync(Problem problem)
    {
        var to = configuration["Ops:AlertEmail"] ?? configuration["SuperUser:Email"];
        if (string.IsNullOrWhiteSpace(to) || !_throttle.TryAcquire("problems", DateTime.UtcNow)) return;

        var html = $"""
            <p><strong>Something broke on ArborKin.</strong></p>
            <p>{WebUtility.HtmlEncode(problem.Message)}</p>
            <p>Source: {WebUtility.HtmlEncode(problem.Source)}<br>Time (UTC): {problem.OccurredAt:u}</p>
            <p>Details are in Admin &gt; Problems. No further email for
            {UploadAlertThrottle.Window.TotalMinutes:0} minutes; later errors are still listed there.</p>
            """;

        using var scope = scopes.CreateScope();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await scope.ServiceProvider.GetRequiredService<IEmailSender>()
            .SendAsync(to, "ArborKin: something broke", html, cts.Token);
    }
}
