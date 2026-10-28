using System.Text.Json;
using Hive.Browser;
using Hive.Core;
using PuppeteerSharp;
var pathIndex = Array.IndexOf(args, "--path");
var root = pathIndex >= 0 && pathIndex + 1 < args.Length ? Path.GetFullPath(args[pathIndex + 1]) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hive", "Browser");
Directory.CreateDirectory(root);
Console.WriteLine("Downloading PuppeteerSharp's pinned Chrome for Testing build...");
var fetcher = new BrowserFetcher(new BrowserFetcherOptions { Path = root, Platform = args.Contains("--win32") ? Platform.Win32 : Platform.Win64 });
var installed = args.Contains("--stable") ? await fetcher.DownloadAsync(BrowserTag.Stable) : await fetcher.DownloadAsync();
var executable = installed.GetExecutablePath();
Console.WriteLine($"Browser build: {installed.BuildId}");
Console.WriteLine($"Executable: {executable}");
try
{
    var provider = new PuppeteerBrowserProvider(executable);
    var testProfile = Path.Combine(root, "smoke-profile-" + Guid.NewGuid().ToString("N"));
    await using (var session = await provider.OpenAsync(new("smoke", "Browser compatibility check", testProfile)))
    {
        await session.NavigateAsync(new Uri("about:blank"));
        await session.RestoreCookiesAsync([new("hive_smoke", "local-test-only", ".roblox.com", "/", null, true, true)]);
        var cookies = await session.ReadCookiesAsync();
        if (cookies.Single(c => c.Name == "hive_smoke").Value != "local-test-only")
            throw new Exception("Cookie context test failed");
        if (args.Contains("--roblox"))
        {
            await session.NavigateAsync(new Uri("https://www.roblox.com/login"));
            Console.WriteLine("PASS: Roblox sign-in page opened through Stealth.");
        }
        Console.WriteLine("PASS: PuppeteerSharp + StealthPlugin launch, isolated context, navigation, cookie restore/read.");
    }
    await File.WriteAllTextAsync(Path.Combine(root, "managed-browser.json"), JsonSerializer.Serialize(new
    {
        ExecutablePath = executable,
        BuildId = installed.BuildId,
        VerifiedAt = DateTimeOffset.UtcNow
    }));
    Console.WriteLine("Managed browser is installed and verified.");

}
catch (Exception error)
{
    var loaderFailure = false;
    for (Exception? current = error; current is not null; current = current.InnerException)
        loaderFailure |= current is System.ComponentModel.Win32Exception { NativeErrorCode: 14001 };
    Console.Error.WriteLine(loaderFailure
        ? "Chrome was downloaded, but Windows rejected startup with SideBySide error 14001. The browser was not selected. Choose a working Chrome/Edge in Hive settings or resolve the Windows loader error and rerun setup."
        : $"Browser verification failed ({error.GetType().Name}). The browser was not selected; rerun setup after checking browser startup.");
    return 1;
}
return 0;
