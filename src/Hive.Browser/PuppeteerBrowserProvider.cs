using Hive.Core;
using PuppeteerSharp;
using PuppeteerExtraSharp.Plugins;
using PuppeteerExtraSharp.Plugins.ExtraStealth;
namespace Hive.Browser;

public sealed class PuppeteerBrowserProvider(string executablePath) : IBrowserProvider
{
    internal static Task<IPage[]> OpenPagesAsync(IBrowserSession session) =>
        session is Session managed ? managed.OpenPagesAsync() : throw new ArgumentException("Not a managed browser session.", nameof(session));
    public async Task<IBrowserSession> OpenAsync(BrowserProfile profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Configure Browser:ExecutablePath before opening a browser.", executablePath);
        Directory.CreateDirectory(profile.DirectoryPath);
        var stealth = new StealthPlugin();
        var plugins = new[] { stealth }.Cast<PuppeteerExtraPlugin>().Concat(stealth.GetDependencies()).OrderBy(p => p.Requirements.Contains(PluginRequirements.RunLast)).ToArray();
        var options = new LaunchOptions { Headless = false, ExecutablePath = executablePath, UserDataDir = profile.DirectoryPath, DefaultViewport = null, Args = ["--window-size=820,600", "--window-position=80,60"], Timeout = 15000 };
        foreach (var plugin in plugins)
            await plugin.BeforeLaunchAsync(options);
        var browser = await Puppeteer.LaunchAsync(options);
        try
        {
            var initialPages = await browser.PagesAsync();
            var context = await browser.CreateBrowserContextAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return new Session(browser, context, plugins, initialPages);
        }
        catch { await browser.CloseAsync(); browser.Dispose(); throw; }
    }
    private sealed class Session(IBrowser browser, IBrowserContext context, IReadOnlyList<PuppeteerExtraPlugin> plugins, IReadOnlyList<IPage> initialPages) : IBrowserSession
    {
        internal Task<IPage[]> OpenPagesAsync() => browser.PagesAsync();
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
            var page = currentPage;
            if (page is null || page.IsClosed)
            {
                page = await context.NewPageAsync();
                currentPage = page;
                foreach (var plugin in plugins)
                    await plugin.OnPageCreatedAsync(page).WaitAsync(cancellationToken);
                foreach (var initialPage in initialPages)
                    if (!initialPage.IsClosed)
                        await initialPage.CloseAsync().WaitAsync(cancellationToken);
                var window = await page.Client.SendAsync("Browser.getWindowForTarget").WaitAsync(cancellationToken);
                var windowId = window!.Value.GetProperty("windowId").GetInt32();
                await page.Client.SendAsync("Browser.setWindowBounds", new
                {
                    windowId,
                    bounds = new
                    {
                        windowState = "normal"
                    }
                }).WaitAsync(cancellationToken);
                await page.Client.SendAsync("Browser.setWindowBounds", new
                {
                    windowId,
                    bounds = new
                    {
                        width = 820,
                        height = 600,
                        left = 80,
                        top = 60
                    }
                }).WaitAsync(cancellationToken);
            }
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
            // Leave the final submit and any verification to the person using the browser.
        }
        public async Task AcceptCookieConsentAsync(CancellationToken ct = default)
        {
            var page = currentPage;
            if (page is null || page.IsClosed || !Uri.TryCreate(page.Url, UriKind.Absolute, out var address) || address.Scheme != "https" || (address.Host != "www.roblox.com" && address.Host != "roblox.com")) return;
            try
            {
                await page.EvaluateExpressionAsync(@"(() => {
                    const button = document.querySelector('#onetrust-accept-btn-handler');
                    if (button && button.getClientRects().length && !button.disabled) button.click();
                })()").WaitAsync(ct);
            }
            catch (PuppeteerException) when (!ct.IsCancellationRequested) { /* Navigation can replace the page while the banner is checked. */ }
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
            catch (PuppeteerException) { }
            catch (ObjectDisposedException) { }
            finally { browser.Dispose(); }
        }
    }
}
