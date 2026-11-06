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
                try
                {
                var window = await page.Client.SendAsync("Browser.getWindowForTarget").WaitAsync(cancellationToken);
                if (window is null || !window.Value.TryGetProperty("windowId", out var windowIdValue)) throw new InvalidOperationException("Window bounds are unavailable.");
                var windowId = windowIdValue.GetInt32();
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
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is PuppeteerException or InvalidOperationException) { /* Browser can still open if native window sizing is unavailable. */ }
            }
            try
            {
                var response = await page.GoToAsync(uri.AbsoluteUri, new NavigationOptions { WaitUntil = [WaitUntilNavigation.DOMContentLoaded], Timeout = 60000 }).WaitAsync(cancellationToken);
                if ((uri.Host == "www.roblox.com" || uri.Host == "roblox.com") && uri.AbsolutePath == "/login" && response is not null)
                {
                    if ((int)response.Status == 429) throw new BrowserRateLimitException();
                    if ((int)response.Status >= 500) throw new BrowserPageLoadException("Roblox sign-in is temporarily unavailable.");
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is PuppeteerException or TimeoutException) { throw new BrowserPageLoadException("The browser page did not finish loading.", ex); }
        }
        public async Task FillLoginAsync(string username, string password, CancellationToken ct = default)
        {
            var page = currentPage ?? throw new InvalidOperationException("Navigate before filling sign-in.");
            EnsureLoginPage(page);
            try
            {
                const string usernameSelector = "#login-username, input[name='username'], input[autocomplete='username'], input[placeholder*='username' i]";
                const string passwordSelector = "#login-password, input[name='password'], input[autocomplete='current-password'], input[type='password']";
                await using var userInput = await page.WaitForSelectorAsync(usernameSelector, new WaitForSelectorOptions { Timeout = 20000, Visible = true }).WaitAsync(ct);
                await using var passwordInput = await page.WaitForSelectorAsync(passwordSelector, new WaitForSelectorOptions { Timeout = 20000, Visible = true }).WaitAsync(ct);
                if (userInput is null || passwordInput is null) throw new BrowserPageLoadException("The sign-in form did not finish loading.");
                async Task ReplaceAsync(IElementHandle input, string value)
                {
                    EnsureLoginPage(page);
                    await input.FocusAsync().WaitAsync(ct);
                    await page.Keyboard.DownAsync("Control").WaitAsync(ct);
                    try { await page.Keyboard.PressAsync("A").WaitAsync(ct); }
                    finally { await page.Keyboard.UpAsync("Control"); }
                    EnsureLoginPage(page);
                    await input.TypeAsync(value).WaitAsync(ct);
                    if (!await input.EvaluateFunctionAsync<bool>("(input, expected) => input.value === expected", value).WaitAsync(ct)) throw new BrowserPageLoadException("The sign-in form changed while it was being filled.");
                }
                await ReplaceAsync(userInput, username);
                await ReplaceAsync(passwordInput, password);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is PuppeteerException or TimeoutException) { throw new BrowserPageLoadException("The sign-in form did not finish loading.", ex); }
        }
        private static void EnsureLoginPage(IPage page)
        {
            if (!Uri.TryCreate(page.Url, UriKind.Absolute, out var address) || address.Scheme != "https" || (address.Host != "www.roblox.com" && address.Host != "roblox.com") || (address.AbsolutePath != "/login" && address.AbsolutePath != "/login/")) throw new InvalidOperationException("Credentials can only be used on Roblox sign-in.");
        }
        public async Task SubmitLoginAsync(CancellationToken ct = default)
        {
            var page = currentPage ?? throw new InvalidOperationException("Navigate before submitting sign-in.");
            EnsureLoginPage(page);
            for (var attempt = 0; attempt < 120; attempt++)
            {
                EnsureLoginPage(page);
                await AcceptCookieConsentAsync(ct);
                var unobstructed = await page.EvaluateExpressionAsync<bool>(@"(() => {
                    const button = document.querySelector('#login-button') || Array.from(document.querySelectorAll('button')).find(b => /^(sign in|log in)$/i.test((b.innerText || '').trim()));
                    if (!button || button.disabled || button.getAttribute('aria-disabled') === 'true' || !button.getClientRects().length) return false;
                    button.scrollIntoView({ block: 'center' });
                    const r = button.getBoundingClientRect();
                    const top = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
                    if (!top || (top !== button && !button.contains(top))) return false;
                    button.setAttribute('data-hive-sign-in', 'true');
                    return true;
                })()").WaitAsync(ct);
                if (unobstructed) { await page.ClickAsync("[data-hive-sign-in='true']").WaitAsync(ct); return; }
                await Task.Delay(250, ct);
            }
            throw new TimeoutException("Sign-in button is covered. Complete sign-in in the browser.");
        }
        public async Task AcceptCookieConsentAsync(CancellationToken ct = default)
        {
            var page = currentPage;
            if (page is null || page.IsClosed || !Uri.TryCreate(page.Url, UriKind.Absolute, out var address) || address.Scheme != "https" || (address.Host != "www.roblox.com" && address.Host != "roblox.com")) return;
            try
            {
                await page.EvaluateExpressionAsync(@"(() => {
                    const button = document.querySelector('#onetrust-accept-btn-handler') || Array.from(document.querySelectorAll('button')).find(b => /^accept all$/i.test((b.innerText || '').trim()));
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
