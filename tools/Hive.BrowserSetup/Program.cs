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
        if ((await PuppeteerBrowserProvider.OpenPagesAsync(session)).Length != 1)
            throw new Exception("Expected one browser page after navigation.");
        var managedPage = (await PuppeteerBrowserProvider.OpenPagesAsync(session)).Single();
        var browserWindow = await managedPage.Client.SendAsync("Browser.getWindowForTarget");
        var bounds = browserWindow!.Value.GetProperty("bounds");
        if (bounds.GetProperty("windowState").GetString() != "normal" || bounds.GetProperty("width").GetInt32() != 820 || bounds.GetProperty("height").GetInt32() != 600)
            throw new Exception("Browser did not start in its compact window bounds.");
        await session.RestoreCookiesAsync([new("hive_smoke", "local-test-only", ".roblox.com", "/", null, true, true)]);
        var cookies = await session.ReadCookiesAsync();
        if (cookies.Single(c => c.Name == "hive_smoke").Value != "local-test-only")
            throw new Exception("Cookie context test failed");
        if (args.Contains("--roblox"))
        {
            await session.NavigateAsync(new Uri("https://www.roblox.com/login"));
            if ((await PuppeteerBrowserProvider.OpenPagesAsync(session)).Length != 1)
                throw new Exception("Repeated navigation opened an extra browser page.");
            await managedPage.WaitForSelectorAsync("#login-username", new WaitForSelectorOptions { Timeout = 20000 });
            await managedPage.WaitForSelectorAsync("#login-password", new WaitForSelectorOptions { Timeout = 20000 });
            await managedPage.WaitForSelectorAsync("#login-button", new WaitForSelectorOptions { Timeout = 20000 });
            await Task.Delay(3000);
            var consentVisible = await managedPage.EvaluateExpressionAsync<bool>("!!document.querySelector('#onetrust-accept-btn-handler')?.getClientRects().length");
            if (consentVisible)
            {
                await session.AcceptCookieConsentAsync();
                await Task.Delay(500);
                if (await managedPage.EvaluateExpressionAsync<bool>("!!document.querySelector('#onetrust-accept-btn-handler')?.getClientRects().length")) throw new Exception("Cookie consent banner remained visible.");
                Console.WriteLine("PASS: Roblox cookie-consent banner accepted.");
            }
            else
            {
                await managedPage.EvaluateExpressionAsync("(() => { const b = document.createElement('button'); b.id = 'onetrust-accept-btn-handler'; b.textContent = 'test'; b.onclick = () => b.remove(); document.body.appendChild(b); })()");
                await session.AcceptCookieConsentAsync();
                if (await managedPage.EvaluateExpressionAsync<bool>("!!document.querySelector('#onetrust-accept-btn-handler')")) throw new Exception("Cookie-consent button was not activated.");
                Console.WriteLine("PASS: consent button handling on a simulated banner; Roblox did not show its own banner in this session.");
            }
            Console.WriteLine("PASS: Roblox sign-in page opened through Stealth.");
            await managedPage.SetContentAsync("<html><body><form onsubmit='event.preventDefault(); window.hiveSubmitted = (window.hiveSubmitted || 0) + 1'><input id='login-username'><input id='login-password' type='password'><button id='login-button' type='submit'>Log In</button></form></body></html>");
            await session.FillLoginAsync("fixture-user", "fixture-password");
            await session.SubmitLoginAsync();
            if (!await managedPage.EvaluateExpressionAsync<bool>("window.hiveSubmitted === 1 && document.querySelector('#login-username').value === 'fixture-user' && document.querySelector('#login-password').value === 'fixture-password'")) throw new Exception("Sign-in form was not filled and submitted once.");
            Console.WriteLine("PASS: sign-in fields and one automatic submission on a simulated form (no real account credentials).");
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
