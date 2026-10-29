using System.IO;
using System.Net;
using System.Net.Http;
using Hive.App;
using Hive.Core;
using Hive.Infrastructure;
using Hive.Roblox;
using Hive.Storage;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ProfileChecks
{
    public static async Task RunAsync(string folder)
    {
        var database = new HiveDatabase(Path.Combine(folder,"profiles.db")); await database.InitializeAsync(); await database.InitializeAsync();
        var accounts = new AccountRepository(database); var profiles = new BrowserProfileRepository(database); var sessions = new SessionStore(database,new DpapiSecretProtector()); var credentials = new CredentialStore(database,new DpapiSecretProtector());
        await accounts.SaveAsync(new("profile-account","ProfileUser","",null,"",SessionState.Valid,901));
        await sessions.SaveAsync("profile-account",new([new(".ROBLOSECURITY","test-profile-cookie",".roblox.com","/",null,true,true)],DateTimeOffset.UtcNow));
        var preferencePath = Path.Combine(folder,"profile-preferences.json"); var preferences = new AppPreferences { StoragePath = preferencePath, AutoCheckSessions = false };
        var manager = new SessionManager(sessions,accounts,_ => new Identity()); var browser = new BrowserFixture();
        AccountsViewModel Vm() => new(accounts,credentials,manager,new AccountActivityTracker(),browser,preferences,NullLogger<AccountsViewModel>.Instance,sessionStore:sessions,profileRepository:profiles,profileDirectory:Path.Combine(folder,"profile-cache"));
        var vm = Vm(); await vm.LoadAsync(); await vm.CreateProfileAsync("Main profile");
        var profile = (await profiles.GetAllAsync()).Single();
        await vm.RenameProfileAsync(profile,"Renamed"); profile = (await profiles.GetAllAsync()).Single();
        if (profile.Name != "Renamed" || profile.AccountId != "profile-account") throw new Exception("Profile metadata did not persist");
        await vm.OpenProfileAsync(profile);
        if (browser.OpenCount != 1 || browser.Session.Cookies.Single().Value != "test-profile-cookie" || (await profiles.GetAllAsync()).Single().LastUsedAt is null) throw new Exception("Profile session restoration or last-used persistence failed");
        await vm.RemoveProfileAsync(profile,false);
        if ((await profiles.GetAllAsync()).Count != 1) throw new Exception("An open profile was deleted");
        await vm.OpenAccountSettingsAsync(); if (browser.OpenCount != 1 || browser.Session.Address?.AbsoluteUri != "https://www.roblox.com/my/account") throw new Exception("Account settings did not reuse its managed browser");
        await vm.ShutdownAsync();
        vm = Vm(); await vm.LoadAsync(); Directory.CreateDirectory(profile.DirectoryPath); await File.WriteAllTextAsync(Path.Combine(profile.DirectoryPath,"cache-marker"),"test cache");
        await vm.RemoveProfileAsync(profile,true);
        if (Directory.Exists(profile.DirectoryPath) || (await profiles.GetAllAsync()).Count != 1 || await sessions.ReadAsync("profile-account") is null) throw new Exception("Profile reset lost the account session or failed to archive cache");
        await vm.RemoveProfileAsync(profile,false); if ((await profiles.GetAllAsync()).Count != 0) throw new Exception("Profile removal failed");
        var outside = Path.Combine(folder,"unowned-cache"); Directory.CreateDirectory(outside); var invalid = new BrowserProfile("outside","Outside",outside,"profile-account"); await profiles.SaveAsync(invalid); await vm.RemoveProfileAsync(invalid,false);
        if (!Directory.Exists(outside) || (await profiles.GetAllAsync()).Count != 1) throw new Exception("Profile path ownership guard failed");
        vm.LaunchMode = LaunchKind.PrivateServer; vm.LaunchTargetId = "123"; vm.LaunchArgument = "https://www.roblox.com/games/123/test?privateServerLinkCode=private-test-code"; vm.SaveCurrentPlace(); vm.SaveCurrentPlace();
        var json = await File.ReadAllTextAsync(preferencePath);
        if (json.Contains("private-test-code") || preferences.SavedPlaces.Count != 1) throw new Exception("Private invite was stored in plaintext or duplicated");
        var reopened = AppPreferences.Load(preferencePath);
        if (!reopened.PrivateInvite.Contains("private-test-code") || reopened.LaunchKind != LaunchKind.PrivateServer) throw new Exception("Encrypted private target did not survive preferences reload");
        await accounts.DeleteAsync("profile-account"); if ((await profiles.GetAllAsync()).Count != 0) throw new Exception("Account deletion did not cascade to profile metadata"); await vm.ShutdownAsync();
        Console.WriteLine("PASS: browser-profile CRUD, metadata persistence, account session restoration, open-profile protection, cache archive/reset, path guard, cascade deletion and encrypted saved private targets.");
    }
    private sealed class Identity : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":901,\"name\":\"ProfileUser\"}") });
    }
    private sealed class BrowserFixture : IBrowserProvider
    {
        public int OpenCount; public SessionFixture Session { get; } = new();
        public Task<IBrowserSession> OpenAsync(BrowserProfile profile,CancellationToken ct = default) { OpenCount++; return Task.FromResult<IBrowserSession>(Session); }
    }
    private sealed class SessionFixture : IBrowserSession
    {
        public bool IsOpen { get; private set; } = true; public Uri? Address; public IReadOnlyList<SessionCookie> Cookies = [];
        public Task NavigateAsync(Uri uri,CancellationToken ct = default) { Address = uri; return Task.CompletedTask; }
        public Task RestoreCookiesAsync(IReadOnlyList<SessionCookie> cookies,CancellationToken ct = default) { Cookies = cookies; return Task.CompletedTask; }
        public Task<IReadOnlyList<SessionCookie>> ReadCookiesAsync(CancellationToken ct = default) => Task.FromResult(Cookies);
        public ValueTask DisposeAsync() { IsOpen = false; return ValueTask.CompletedTask; }
    }
}
