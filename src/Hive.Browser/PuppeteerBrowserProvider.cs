using Hive.Core;
using PuppeteerSharp;
using PuppeteerExtraSharp.Plugins;
using PuppeteerExtraSharp.Plugins.ExtraStealth;
namespace Hive.Browser;

public sealed class PuppeteerBrowserProvider(string executablePath) : IBrowserProvider
{
    public async Task<IBrowserSession> OpenAsync(BrowserProfile profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Configure Browser:ExecutablePath before opening a browser.", executablePath);
        Directory.CreateDirectory(profile.DirectoryPath);
        var stealth = new StealthPlugin();
        var plugins = new[] { stealth }.Cast<PuppeteerExtraPlugin>().Concat(stealth.GetDependencies()).OrderBy(p => p.Requirements.Contains(PluginRequirements.RunLast)).ToArray();
        var options = new LaunchOptions { Headless = false, ExecutablePath = executablePath, UserDataDir = profile.DirectoryPath, DefaultViewport = null, Timeout = 15000 };
        foreach (var plugin in plugins)
            await plugin.BeforeLaunchAsync(options);
        var browser = await Puppeteer.LaunchAsync(options);
        try
        {
            var context = await browser.CreateBrowserContextAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return new Session(browser, context, plugins);
        }
        catch { await browser.CloseAsync(); browser.Dispose(); throw; }
    }
    private sealed class Session(IBrowser browser, IBrowserContext context, IReadOnlyList<PuppeteerExtraPlugin> plugins) : IBrowserSession
    {
        private IPage? currentPage;
        private int disposed;
        public bool IsOpen => disposed == 0 && browser.IsConnected;
        public async Task RestoreCookiesAsync(IReadOnlyList<SessionCookie> cookies, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var permitted = cookies.Where(c => (c.Domain == ".roblox.com" || c.Domain == "roblox.com") && (c.Expires is null or <= 0 || c.Expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            await context.SetCookieAsync(permitted.Select(c => new CookieData { Name = c.Name, Value = c.Value, Domain = c.Domain, Path = c.Path, Secure = c.Secure, HttpOnly = c.HttpOnly, Expires = c.Expires is > 0 ? c.Expires : null }).ToArray());
        }
        public async Task NavigateAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await context.NewPageAsync();
            currentPage = page;
            // Apply and await Stealth on the managed page before its first navigation.
            foreach (var plugin in plugins)
                await plugin.OnPageCreatedAsync(page).WaitAsync(cancellationToken);
            await page.GoToAsync(uri.AbsoluteUri, new NavigationOptions { WaitUntil = [WaitUntilNavigation.DOMContentLoaded], Timeout = 60000 }).WaitAsync(cancellationToken);
        }
        public async Task FillLoginAsync(string username, string password, CancellationToken ct = default)
        {
            var page = currentPage ?? throw new InvalidOperationException("Navigate before filling sign-in.");
            if (!Uri.TryCreate(page.Url, UriKind.Absolute, out var address) || address.Scheme != "https" || (address.Host != "www.roblox.com" && address.Host != "roblox.com") || !address.AbsolutePath.StartsWith("/login", StringComparison.Ordinal))
                throw new InvalidOperationException("Credentials can only be filled on Roblox sign-in.");
            await page.WaitForSelectorAsync("#login-username", new WaitForSelectorOptions { Timeout = 20000 }).WaitAsync(ct);
            await page.TypeAsync("#login-username", username).WaitAsync(ct);
            await page.TypeAsync("#login-password", password).WaitAsync(ct);
            await page.ClickAsync("#login-button").WaitAsync(ct);
        }
        public async Task ExecuteScriptAsync(string script, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(script))
                return;
            var page = currentPage ?? throw new InvalidOperationException("Navigate before running a script.");
            await page.EvaluateExpressionAsync(script).WaitAsync(ct);
        }
        public async Task<IReadOnlyList<SessionCookie>> ReadCookiesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return (await context.GetCookiesAsync().WaitAsync(cancellationToken)).Where(c => c.Domain == ".roblox.com" || c.Domain == "roblox.com").Select(c => new SessionCookie(c.Name, c.Value, c.Domain, c.Path, c.Expires, c.Secure ?? false, c.HttpOnly ?? false)).ToList();
        }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;
            try
            {
                await browser.CloseAsync();
            }
            finally { browser.Dispose(); }
        }
    }
}
