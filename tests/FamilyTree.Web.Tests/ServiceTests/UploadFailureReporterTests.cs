using FamilyTree.Core.Services;
using FamilyTree.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FamilyTree.Web.Tests.ServiceTests;

public class UploadFailureReporterTests
{
    private const long Max = 8L * 1024 * 1024;
    private static readonly IOException Error = new("could not <read>");

    private sealed class RecordingEmail(bool fails = false) : IEmailSender
    {
        public List<(string To, string Body)> Sent { get; } = [];

        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            if (fails) throw new InvalidOperationException("SMTP down");
            Sent.Add((to, htmlBody));
            return Task.CompletedTask;
        }
    }

    private static UploadFailureReporter Reporter(IEmailSender email, params (string, string)[] config) =>
        new(email,
            new ConfigurationBuilder()
                .AddInMemoryCollection(config.ToDictionary(c => c.Item1, c => (string?)c.Item2))
                .Build(),
            new FakeCurrentUserService { UserId = Guid.NewGuid(), Email = "relative@example.com" },
            new UploadAlertThrottle(),
            NullLogger<UploadFailureReporter>.Instance);

    [Fact]
    public async Task EmailsTheOpsAddressOnce_HoweverOftenThePersonRetries()
    {
        var email = new RecordingEmail();
        var reporter = Reporter(email, ("Ops:AlertEmail", "ops@example.com"), ("SuperUser:Email", "su@example.com"));

        await reporter.ReportAsync(new UnreadableFile(), Max, Error);
        await reporter.ReportAsync(new UnreadableFile(), Max, Error);

        var sent = email.Sent.Should().ContainSingle().Subject;
        sent.To.Should().Be("ops@example.com");
        sent.Body.Should().Contain("relative@example.com").And.Contain("could not &lt;read&gt;");
    }

    [Fact]
    public async Task FallsBackToTheSuperUser()
    {
        var email = new RecordingEmail();

        await Reporter(email, ("SuperUser:Email", "su@example.com")).ReportAsync(new UnreadableFile(), Max, Error);

        email.Sent.Should().ContainSingle().Which.To.Should().Be("su@example.com");
    }

    [Fact]
    public async Task TooBigAFile_IsNotEmailed()
    {
        var email = new RecordingEmail();

        await Reporter(email, ("Ops:AlertEmail", "ops@example.com"))
            .ReportAsync(new UnreadableFile(Max + 1), Max, Error);

        email.Sent.Should().BeEmpty();
        UploadFailure.MessageFor(Max + 1, Max).Should().Be("That photo is too big. The limit is 8 MB.");
    }

    [Fact]
    public async Task AFailingMailServer_DoesNotThrow()
    {
        var reporter = Reporter(new RecordingEmail(fails: true), ("Ops:AlertEmail", "ops@example.com"));

        await reporter.Invoking(r => r.ReportAsync(new UnreadableFile(), Max, Error)).Should().NotThrowAsync();
    }
}
