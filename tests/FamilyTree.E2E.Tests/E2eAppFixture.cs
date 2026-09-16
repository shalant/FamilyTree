using System.Diagnostics;
using FamilyTree.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace FamilyTree.E2E.Tests;

/// <summary>
/// Boots a real instance of FamilyTree.Web as an external OS process (not
/// WebApplicationFactory/TestServer — Program.cs has no public partial class Program
/// marker, and forcing WebApplicationFactory to bind a real Kestrel port is a known-
/// fragile trick). Zero changes needed to how the app actually runs; this matches
/// deploy-web.yml's real deployment path exactly.
///
/// Shared once across the whole E2E test run via ICollectionFixture — the app boots
/// once, not once per test class.
/// </summary>
public sealed class E2eAppFixture : IAsyncLifetime
{
    private const int Port = 44777;
    public string BaseUrl { get; } = $"https://localhost:{Port}";
    public string ConnectionString { get; private set; } = "";

    /// <summary>
    /// False when the E2E database (or the app itself) couldn't be reached — tests
    /// should call Skip.If(!fixture.IsAvailable, fixture.SkipReason) rather than fail,
    /// so a contributor who hasn't set up the E2E database gets a clear skip instead
    /// of a confusing failure when running the full solution's test suite.
    /// </summary>
    public bool IsAvailable { get; private set; }
    public string SkipReason { get; private set; } = "";

    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;
    public TestDataSeeder Seeder { get; private set; } = null!;

    private Process? _appProcess;
    private readonly object _logLock = new();
    public string LogFilePath { get; } = Path.Combine(Path.GetTempPath(), $"e2e-app-{Guid.NewGuid():N}.log");

    private void AppendLog(string? line)
    {
        if (line is null) return;
        lock (_logLock) File.AppendAllText(LogFilePath, line + "\n");
    }

    public async Task InitializeAsync()
    {
        ConnectionString = Environment.GetEnvironmentVariable("E2E_ConnectionString")
            ?? @"Server=localhost\SQLEXPRESS;Database=FamilyTreeDb_E2E;Trusted_Connection=True;TrustServerCertificate=True";

        if (!await TryResetDatabaseAsync())
            return; // SkipReason already set

        var webDllPath = FindWebAppDll();
        if (webDllPath is null)
        {
            SkipReason = "Could not locate a built FamilyTree.Web.dll next to this test assembly's own " +
                         "build configuration. Build the solution (dotnet build FamilyTree.sln) before running E2E tests.";
            return;
        }

        if (!StartAppProcess(webDllPath))
            return; // SkipReason already set

        if (!await WaitUntilReadyAsync())
        {
            SkipReason = "FamilyTree.Web did not become ready within the startup timeout.";
            KillAppProcess();
            return;
        }

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        Seeder = new TestDataSeeder(ConnectionString);

        IsAvailable = true;
    }

    // A fresh, empty, correctly-migrated schema every run — EnsureDeleted then let the
    // app's own unconditional MigrateAsync() (Program.cs) recreate it on startup, the
    // same migration path production actually uses, rather than the harness doing its
    // own separate migration call.
    private async Task<bool> TryResetDatabaseAsync()
    {
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(ConnectionString)
                .Options;
            await using var db = new AppDbContext(options);
            await db.Database.EnsureDeletedAsync();
            return true;
        }
        catch (Exception ex)
        {
            SkipReason = $"E2E test database unreachable at the configured connection string " +
                         $"({ex.GetType().Name}: {ex.Message}). Set the E2E_ConnectionString environment " +
                         $"variable, or ensure a local SQLEXPRESS instance is reachable with a database " +
                         $"named FamilyTreeDb_E2E (created automatically on first successful run).";
            return false;
        }
    }

    // The E2E project deliberately has no ProjectReference to FamilyTree.Web (the app
    // under test runs out-of-process), so its build output has to be located by walking
    // up to the repo root and back down — reusing whichever Debug/Release configuration
    // this very test assembly was itself built with, so `dotnet test -c Release` finds
    // the Release build of the web app rather than a stale or missing Debug one.
    private static string? FindWebAppDll()
    {
        var configuration = AppContext.BaseDirectory.Contains("Release", StringComparison.OrdinalIgnoreCase)
            ? "Release" : "Debug";

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FamilyTree.sln")))
            dir = dir.Parent;

        if (dir is null)
            return null;

        var candidate = Path.Combine(dir.FullName, "src", "FamilyTree.Web", "bin", configuration, "net10.0", "FamilyTree.Web.dll");
        return File.Exists(candidate) ? candidate : null;
    }

    private bool StartAppProcess(string webDllPath)
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{webDllPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        psi.Environment["ASPNETCORE_URLS"] = BaseUrl;

        // The one setting the E2E run actually needs to differ from the developer's own
        // local setup — everything below exists purely to make sure NONE of the
        // developer's real user-secrets (real Gmail SMTP credentials, real Azure Storage
        // account, real SuperUser email) can leak into this run. Program.cs loads user
        // secrets in Development, and (as of this branch) re-adds environment variables
        // afterward specifically so these overrides win — without that fix, all of the
        // below would be silently ignored in favor of the developer's real secrets.json.
        psi.Environment["ConnectionStrings__DefaultConnection"] = ConnectionString;
        psi.Environment["Email__SmtpHost"] = "";              // forces LogEmailSender, never a real send
        psi.Environment["SuperUser__Email"] = "";
        psi.Environment["Google__ClientId"] = "";
        psi.Environment["Google__ClientSecret"] = "";
        psi.Environment["AzureStorage__ConnectionString"] = ""; // forces the UseDevelopmentStorage=true fallback
        psi.Environment["DevAuth__Enabled"] = "false";           // see class remarks: DevAuth replaces the
                                                                  // entire cookie/Identity pipeline, which
                                                                  // would break real registration/login —
                                                                  // this harness authenticates via the real
                                                                  // UI flow instead, on purpose.

        try
        {
            _appProcess = Process.Start(psi);
            if (_appProcess is null)
                return false;

            // Redirecting stdout/stderr without ever reading them risks the child
            // process blocking once the OS pipe buffer fills — a real risk here given
            // how much EF Core SQL logging this app emits per request. Draining both
            // asynchronously into a bounded ring buffer keeps the process healthy and
            // gives a real diagnostic trail (AppLogTail) for the next thing that goes
            // wrong, rather than a silent hang or a guessed-at fix.
            _appProcess.OutputDataReceived += (_, e) => AppendLog(e.Data);
            _appProcess.ErrorDataReceived += (_, e) => AppendLog(e.Data);
            _appProcess.BeginOutputReadLine();
            _appProcess.BeginErrorReadLine();

            return true;
        }
        catch (Exception ex)
        {
            SkipReason = $"Failed to start FamilyTree.Web process: {ex.Message}";
            return false;
        }
    }

    private async Task<bool> WaitUntilReadyAsync()
    {
        using var http = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        });

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (_appProcess is { HasExited: true })
                return false; // crashed on startup — nothing to wait for

            try
            {
                var response = await http.GetAsync(BaseUrl);
                // Any HTTP response at all (even a redirect or error page) means Kestrel
                // is up and routing requests — that's all "ready" needs to mean here.
                return true;
            }
            catch
            {
                await Task.Delay(500);
            }
        }

        return false;
    }

    private void KillAppProcess()
    {
        if (_appProcess is { HasExited: false })
        {
            try { _appProcess.Kill(entireProcessTree: true); } catch { /* best effort */ }
        }
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        Playwright?.Dispose();
        KillAppProcess();
        _appProcess?.Dispose();
    }
}
