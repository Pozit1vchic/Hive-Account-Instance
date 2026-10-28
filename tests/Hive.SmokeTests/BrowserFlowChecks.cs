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
        Console.WriteLine("PASS: browser onboarding cancellation, saved-session restoration, nonblocking managed browser opening and shutdown cleanup (provider fixture).");
    }
    private sealed class IdentityHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":5001,\"name\":\"BrowserControl\"}") });
    }
    private sealed class BrowserFixture : IBrowserProvider
    {
        public SessionFixture Session { get; } = new();
        public Task<IBrowserSession> OpenAsync(BrowserProfile profile, CancellationToken cancellationToken = default) => Task.FromResult<IBrowserSession>(Session);
    }
    private sealed class SessionFixture : IBrowserSession
    {
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
        public Task<IReadOnlyList<SessionCookie>> ReadCookiesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SessionCookie>>([]);
        public ValueTask DisposeAsync()
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }
    }
}
