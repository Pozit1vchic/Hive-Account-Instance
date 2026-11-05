using System.IO;
using Hive.Core;
using Hive.Browser;
namespace Hive.App;

public sealed class ConfiguredBrowserProvider(AppPreferences preferences, string configuredPath) : IBrowserProvider
{
    private readonly SemaphoreSlim browserSetup = new(1, 1);
    private string? resolvedExecutable;
    public async Task<IBrowserSession> OpenAsync(BrowserProfile profile, CancellationToken cancellationToken = default)
    {
        var cached = Volatile.Read(ref resolvedExecutable);
        if (cached is not null && File.Exists(cached)) return await new PuppeteerBrowserProvider(cached).OpenAsync(profile, cancellationToken);
        await browserSetup.WaitAsync(cancellationToken);
        try
        {
            cached = Volatile.Read(ref resolvedExecutable);
            if (cached is null || !File.Exists(cached)) return await OpenCoreAsync(profile, cancellationToken);
        }
        finally { browserSetup.Release(); }
        return await new PuppeteerBrowserProvider(cached).OpenAsync(profile, cancellationToken);
    }
    private async Task<IBrowserSession> OpenCoreAsync(BrowserProfile profile, CancellationToken cancellationToken)
    {
        var managed = "";
        var bundledRoot = Path.Combine(AppContext.BaseDirectory, "Browser");
        var localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hive", "Browser");
        var manifest = new[] { Path.Combine(bundledRoot, "managed-browser.json"), Path.Combine(localRoot, "managed-browser.json") }.FirstOrDefault(File.Exists) ?? "";
        if (File.Exists(manifest))
        {
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));
                if (json.RootElement.TryGetProperty("ExecutablePath", out var path) && path.ValueKind == System.Text.Json.JsonValueKind.String)
                    managed = path.GetString() ?? "";
            }
            catch (System.Text.Json.JsonException) { }
            catch (IOException) { }
        }
        var candidates = new[]{managed,configuredPath,preferences.BrowserPath,
   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Google","Chrome","Application","chrome.exe"),
   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft","Edge","Application","msedge.exe"),
   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Google","Chrome","Application","chrome.exe")};
        var additional = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe") };
        var executable = candidates.Concat(additional).FirstOrDefault(File.Exists);
        if (executable is null)
        {
            // Keep the tested browser next to a portable Hive installation when writable.
            var root = bundledRoot;
            try
            {
                Directory.CreateDirectory(root);
                using var probe = File.Create(Path.Combine(root, ".write-probe"), 1, FileOptions.DeleteOnClose);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { root = localRoot; }
            var fetcher = new PuppeteerSharp.BrowserFetcher(new PuppeteerSharp.BrowserFetcherOptions { Path = root });
            var installed = await fetcher.DownloadAsync().WaitAsync(cancellationToken);
            executable = installed.GetExecutablePath();
            var session = await new PuppeteerBrowserProvider(executable).OpenAsync(profile, cancellationToken);
            try
            {
                await File.WriteAllTextAsync(Path.Combine(root, "managed-browser.json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    ExecutablePath = executable,
                    BuildId = installed.BuildId,
                    VerifiedAt = DateTimeOffset.UtcNow
                }), cancellationToken);
                Volatile.Write(ref resolvedExecutable, executable);
                return session;
            }
            catch { await session.DisposeAsync(); throw; }
        }
        var opened = await new PuppeteerBrowserProvider(executable).OpenAsync(profile, cancellationToken);
        Volatile.Write(ref resolvedExecutable, executable);
        return opened;
    }
}
