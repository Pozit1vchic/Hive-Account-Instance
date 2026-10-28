using System.IO;
using Hive.Core;
using Hive.Browser;
namespace Hive.App;

public sealed class ConfiguredBrowserProvider(AppPreferences preferences, string configuredPath) : IBrowserProvider
{
    public Task<IBrowserSession> OpenAsync(BrowserProfile profile, CancellationToken cancellationToken = default)
    {
        var managed = "";
        var manifest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hive", "Browser", "managed-browser.json");
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
        var candidates = new[]{preferences.BrowserPath,configuredPath,managed,
   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Google","Chrome","Application","chrome.exe"),
   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft","Edge","Application","msedge.exe"),
   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Google","Chrome","Application","chrome.exe")};
        var additional = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe") };
        var executable = candidates.Concat(additional).FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("No configured Chromium browser.");
        return new PuppeteerBrowserProvider(executable).OpenAsync(profile, cancellationToken);
    }
}
