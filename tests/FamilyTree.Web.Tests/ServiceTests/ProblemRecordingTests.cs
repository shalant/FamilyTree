using FamilyTree.Core.Data;
using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Web.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FamilyTree.Web.Tests.ServiceTests;

public class ProblemRecordingTests
{
    private static List<PendingProblem> Drain(ProblemQueue queue)
    {
        var items = new List<PendingProblem>();
        while (queue.Reader.TryRead(out var item)) items.Add(item);
        return items;
    }

    private sealed class Recorder : IToastProblemRecorder
    {
        public List<string> Messages { get; } = [];
        public void Record(string message) => Messages.Add(message);
    }

    [Fact]
    public void OnlyRedToastsAreRecorded()
    {
        var recorder = new Recorder();
        var toasts = new ToastService(recorder);

        toasts.Success("saved");
        toasts.Warning("careful");
        toasts.Error("You do not have permission to update this person.");

        recorder.Messages.Should().Equal("You do not have permission to update this person.");
    }

    [Fact]
    public void LoggedErrorsAreQueuedForEmail_ButNotNoiseOrUploadFailuresTwice()
    {
        var queue = new ProblemQueue();
        var provider = new ProblemLoggerProvider(queue);
        var service = provider.CreateLogger("FamilyTree.Core.Services.PersonService");

        service.LogWarning("only a warning");
        service.LogError(new OperationCanceledException(), "tab closed");
        provider.CreateLogger("Microsoft.EntityFrameworkCore.Update").LogError("duplicate of the next");
        service.LogError(new InvalidOperationException("boom"), "Error updating person");
        provider.CreateLogger(typeof(UploadFailureReporter).FullName!).LogError("Upload failed");

        var queued = Drain(queue);
        queued.Select(q => (q.Problem.Message, q.Email)).Should().Equal(
            ("Error updating person", true), ("Upload failed", false));
        queued[0].Problem.Kind.Should().Be(Problem.Unexpected);
        queued[0].Problem.Detail.Should().Contain("boom");
    }

    [Theory]
    [InlineData("https://arborkin.com/story/respond/abc123XYZ", "/story/respond/…")]
    [InlineData("https://arborkin.com/register?invite=abc123", "/register")]
    [InlineData("https://arborkin.com/people/add", "/people/add")]
    public void PagesAreStoredWithoutTokens(string uri, string expected) =>
        ProblemText.Page(uri).Should().Be(expected);

    [Fact]
    public void TokensInMessagesAreScrubbed() =>
        ProblemText.Scrub("GET /register?invite=203dabc3&x=1 failed").Should().Be("GET /register?invite=…&x=1 failed");

    private sealed class Email : IEmailSender
    {
        public List<string> Bodies { get; } = [];

        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            Bodies.Add(htmlBody);
            return Task.CompletedTask;
        }
    }

    private sealed class Factory(string name, bool broken = false) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => broken
            ? throw new InvalidOperationException("database down")
            : new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options);
    }

    private static ProblemWriter Writer(IDbContextFactory<AppDbContext> factory, Email email) => new(
        new ProblemQueue(),
        factory,
        new ServiceCollection().AddSingleton<IEmailSender>(email).BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>(),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SuperUser:Email"] = "su@example.com" })
            .Build(),
        NullLogger<ProblemWriter>.Instance);

    [Fact]
    public async Task Writer_Stores_PurgesOldReports_AndEmailsOnce()
    {
        var factory = new Factory(Guid.NewGuid().ToString());
        await using (var ctx = factory.CreateDbContext())
        {
            ctx.Problems.Add(new Problem { Message = "old", OccurredAt = DateTime.UtcNow.AddDays(-91) });
            await ctx.SaveChangesAsync();
        }
        var email = new Email();
        var writer = Writer(factory, email);

        await writer.ProcessAsync(new PendingProblem(new Problem { Message = "<first>" }, true));
        await writer.ProcessAsync(new PendingProblem(new Problem { Message = "second" }, true));

        await using var check = factory.CreateDbContext();
        check.Problems.Select(p => p.Message).Should().BeEquivalentTo("<first>", "second");
        email.Bodies.Should().ContainSingle().Which.Should().Contain("&lt;first&gt;");
    }

    [Fact]
    public async Task Writer_StillEmails_WhenTheDatabaseIsDown()
    {
        var email = new Email();

        await Writer(new Factory("down", broken: true), email)
            .ProcessAsync(new PendingProblem(new Problem { Message = "db" }, true));

        email.Bodies.Should().ContainSingle();
    }
}
