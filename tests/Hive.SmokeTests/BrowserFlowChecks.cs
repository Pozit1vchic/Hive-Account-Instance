using System.IO;
using System.Net;
using System.Net.Http;
using Hive.App;
using Hive.Core;
using Hive.Infrastructure;
using Hive.Roblox;
using Hive.Storage;
using Microsoft.Extensions.Logging.Abstractions;

internal static class BrowserFlowChecks
{
    public static async Task RunAsync(HiveDatabase database, AccountRepository accounts, SessionStore sessions, string folder)
    {
        var preferences = new AppPreferences { AutoCheckSessions = false, StoragePath = Path.Combine(folder, "browser-test-preferences.json") };
        var credentials = new CredentialStore(database, new DpapiSecretProtector());
        var manager = new SessionManager(sessions, accounts, _ => new IdentityHandler());
        var onboarding = new AccountOnboardingService(accounts, sessions, _ => new IdentityHandler());
        var provider = new BrowserFixture();
        var vm = new AccountsViewModel(accounts, credentials, manager, new AccountActivityTracker(), provider, preferences, NullLogger<AccountsViewModel>.Instance, onboarding, sessions);
        var pending = ((AsyncCommand)vm.AddBrowserCommand).ExecuteAsync();
        await provider.Session.Navigated.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await ((AsyncCommand)vm.CancelLoginCommand).ExecuteAsync();
        await pending.WaitAsync(TimeSpan.FromSeconds(3));
        if (vm.IsBusy || provider.Session.IsOpen || (await accounts.GetAllAsync()).Count != 0)
            throw new Exception("Cancelled browser onboarding did not clean up");
        await vm.ShutdownAsync();
        var account = new Account("browser-flow", "BrowserControl", "", null, "", SessionState.Valid, 5001);
        await accounts.SaveAsync(account);
        await sessions.SaveAsync(account.Id, new([new(".ROBLOSECURITY", "browser-flow-token", ".roblox.com", "/", null, true, true)], DateTimeOffset.UtcNow));
        provider = new BrowserFixture();
        vm = new AccountsViewModel(accounts, credentials, manager, new AccountActivityTracker(), provider, preferences, NullLogger<AccountsViewModel>.Instance, onboarding, sessions);
        await vm.LoadAsync();
        vm.Selected = vm.Accounts.Single();
        await ((AsyncCommand)vm.OpenBrowserCommand).ExecuteAsync();
        if (vm.IsBusy || provider.Session.Restored.Single().Value != "browser-flow-token" || provider.Session.LastUri?.AbsoluteUri != "https://www.roblox.com/home")
            throw new Exception("Saved-session browser restoration failed");
        await vm.ShutdownAsync();
        if (provider.Session.IsOpen)
            throw new Exception("Application shutdown left a browser open");
        await accounts.DeleteAsync(account.Id);
        provider = new BrowserFixture { CompleteSignIn = true };
        provider.Session.FailedNavigations = 1;
        provider.Session.FailedFills = 1;
        vm = new AccountsViewModel(accounts, credentials, manager, new AccountActivityTracker(), provider, preferences, NullLogger<AccountsViewModel>.Instance, onboarding, sessions);
        await vm.AddCredentialsAsync([new("BrowserControl", "local-test-password")]).WaitAsync(TimeSpan.FromSeconds(8));
        var savedAccount = (await accounts.GetAllAsync()).Single();
        if (vm.LastAddedCount != 1 || provider.Session.NavigateCalls != 3 || provider.Session.FilledUsername != "BrowserControl" || provider.Session.SubmitCalls != 1 || await credentials.ReadPasswordAsync(savedAccount.Id) != "local-test-password")
            throw new Exception("User:Pass confirmation did not reach browser login and encrypted persistence");
        await vm.ShutdownAsync();
        await accounts.DeleteAsync(savedAccount.Id);
        var brokenPage = new SessionFixture { FailedNavigations = 3 };
        try { await LoginPageLoader.PrepareAsync(brokenPage, new("local", "test")); throw new Exception("Broken page was accepted"); }
        catch (BrowserPageLoadException) { }
        if (brokenPage.NavigateCalls != 3 || brokenPage.SubmitCalls != 0) throw new Exception("Page retries were not bounded before submission");
        var cancelledPage = new SessionFixture { FailedNavigations = 3 };
        using (var cancel = new CancellationTokenSource(100))
        {
            try { await LoginPageLoader.PrepareAsync(cancelledPage, new("local", "test"), ct: cancel.Token); throw new Exception("Page retry ignored cancellation"); }
            catch (OperationCanceledException) { }
        }
        if (cancelledPage.NavigateCalls != 1) throw new Exception("Cancelled page was reloaded again");
        var limitedPage = new SessionFixture { RateLimited = true };
        try { await LoginPageLoader.PrepareAsync(limitedPage, new("local", "test")); throw new Exception("Rate limit was ignored"); }
        catch (BrowserRateLimitException) { }
        if (limitedPage.NavigateCalls != 1 || limitedPage.SubmitCalls != 0) throw new Exception("Rate-limited login was retried");
        var parallel = new ParallelBrowsers();
        vm = new AccountsViewModel(accounts, credentials, manager, new AccountActivityTracker(), parallel, preferences, NullLogger<AccountsViewModel>.Instance, onboarding, sessions);
        var batch = vm.AddCredentialsAsync([new("first", "test"), new("second", "test"), new("third", "test")]);
        await parallel.TwoOpened.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (parallel.Sessions.Count != 2) throw new Exception("More than two browser sign-ins opened concurrently");
        await ((AsyncCommand)vm.CancelLoginCommand).ExecuteAsync();
        await batch.WaitAsync(TimeSpan.FromSeconds(3));
        if (parallel.Sessions.Any(s => s.IsOpen) || parallel.Sessions.Any(s => s.ConsentCalls == 0)) throw new Exception("Parallel cancellation or cookie consent handling failed");
        await vm.ShutdownAsync();
        vm = new AccountsViewModel(accounts, credentials, manager, new AccountActivityTracker(), new MissingBrowser(), preferences, NullLogger<AccountsViewModel>.Instance, onboarding, sessions);
        await vm.AddCredentialsAsync([new("BrowserControl", "local-test-password")]);
        if (vm.IsBusy || vm.LastAddedCount != 0 || !vm.Status.Contains("managed browser") || (await accounts.GetAllAsync()).Count != 0)
            throw new Exception("Unavailable browser was not reported without adding a phantom account");
        await vm.ShutdownAsync();
        Console.WriteLine("PASS: browser onboarding cancellation, saved-session restoration, nonblocking managed browser opening and shutdown cleanup (provider fixture).");
    }
    private sealed class IdentityHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":5001,\"name\":\"BrowserControl\"}") });
    }
    private sealed class MissingBrowser : IBrowserProvider
    {
        public Task<IBrowserSession> OpenAsync(BrowserProfile profile, CancellationToken cancellationToken = default) => throw new FileNotFoundException();
    }
    private sealed class BrowserFixture : IBrowserProvider
    {
        public bool CompleteSignIn
        {
            get => Session.CompleteSignIn; init => Session.CompleteSignIn = value;
        }
        public SessionFixture Session { get; } = new();
        public Task<IBrowserSession> OpenAsync(BrowserProfile profile, CancellationToken cancellationToken = default) => Task.FromResult<IBrowserSession>(Session);
    }
    private sealed class ParallelBrowsers : IBrowserProvider
    {
        public List<SessionFixture> Sessions { get; } = [];
        public TaskCompletionSource TwoOpened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IBrowserSession> OpenAsync(BrowserProfile profile, CancellationToken cancellationToken = default)
        {
            var session = new SessionFixture(); Sessions.Add(session); if (Sessions.Count == 2) TwoOpened.TrySetResult();
            return Task.FromResult<IBrowserSession>(session);
        }
    }
    private sealed class SessionFixture : IBrowserSession
    {
        public int ConsentCalls { get; private set; }
        public int SubmitCalls { get; private set; }
        public int NavigateCalls { get; private set; }
        public int FailedNavigations { get; set; }
        public int FailedFills { get; set; }
        public bool RateLimited { get; set; }
        public Task SubmitLoginAsync(CancellationToken ct = default) { SubmitCalls++; return Task.CompletedTask; }
        public Task AcceptCookieConsentAsync(CancellationToken ct = default) { ConsentCalls++; return Task.CompletedTask; }
        public bool CompleteSignIn
        {
            get; set;
        }
        public string? FilledUsername
        {
            get; private set;
        }
        public Task FillLoginAsync(string username, string password, CancellationToken ct = default)
        {
            if (FailedFills-- > 0) throw new BrowserPageLoadException("Incomplete test form");
            FilledUsername = username;
            return Task.CompletedTask;
        }
        public bool IsOpen { get; private set; } = true;
        public TaskCompletionSource Navigated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<SessionCookie> Restored { get; private set; } = [];
        public Uri? LastUri
        {
            get; private set;
        }
        public Task NavigateAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            NavigateCalls++;
            if (RateLimited) throw new BrowserRateLimitException();
            if (FailedNavigations-- > 0) throw new BrowserPageLoadException("Incomplete test page");
            LastUri = uri;
            Navigated.TrySetResult();
            return Task.CompletedTask;
        }
        public Task RestoreCookiesAsync(IReadOnlyList<SessionCookie> cookies, CancellationToken cancellationToken = default)
        {
            Restored = cookies;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<SessionCookie>> ReadCookiesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SessionCookie>>(CompleteSignIn ? [new(".ROBLOSECURITY", "test-sign-in-cookie", ".roblox.com", "/", null, true, true)] : []);
        public ValueTask DisposeAsync()
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }
    }
}
