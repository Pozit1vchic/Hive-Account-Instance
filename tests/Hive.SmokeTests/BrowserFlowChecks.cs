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
        vm = new AccountsViewModel(accounts, credentials, manager, new AccountActivityTracker(), provider, preferences, NullLogger<AccountsViewModel>.Instance, onboarding, sessions);
        await vm.AddCredentialsAsync([new("BrowserControl", "local-test-password")]).WaitAsync(TimeSpan.FromSeconds(8));
        var savedAccount = (await accounts.GetAllAsync()).Single();
        if (vm.LastAddedCount != 1 || provider.Session.FilledUsername != "BrowserControl" || await credentials.ReadPasswordAsync(savedAccount.Id) != "local-test-password")
            throw new Exception("User:Pass confirmation did not reach browser login and encrypted persistence");
        await vm.ShutdownAsync();
        await accounts.DeleteAsync(savedAccount.Id);
        vm = new AccountsViewModel(accounts, credentials, manager, new AccountActivityTracker(), new MissingBrowser(), preferences, NullLogger<AccountsViewModel>.Instance, onboarding, sessions);
        await vm.AddCredentialsAsync([new("BrowserControl", "local-test-password")]);
        if (vm.IsBusy || vm.LastAddedCount != 0 || !vm.Status.Contains("Settings > Browser") || (await accounts.GetAllAsync()).Count != 0)
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
    private sealed class SessionFixture : IBrowserSession
    {
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
