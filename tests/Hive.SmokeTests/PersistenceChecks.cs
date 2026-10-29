using System.Diagnostics;
using System.IO;
using Hive.App;
using Hive.Core;
using Hive.Infrastructure;
using Hive.Roblox;
using Hive.Storage;
using Microsoft.Extensions.Logging.Abstractions;

internal static class PersistenceChecks
{
    public static async Task RunAsync(string root)
    {
        var folder = Path.Combine(root, "restart");
        Directory.CreateDirectory(folder);
        var db = new HiveDatabase(Path.Combine(folder, "hive.db"));
        await db.InitializeAsync();
        var accounts = new AccountRepository(db);
        var groups = new AccountGroupRepository(db);
        var credentials = new CredentialStore(db, new DpapiSecretProtector());
        var sessions = new SessionStore(db, new DpapiSecretProtector());
        await accounts.SaveAsync(new("restart-account", "RestartUser", "", null, "Keep description", SessionState.Valid, 701));
        await credentials.SavePasswordAsync("restart-account", "restart-test-password");
        await sessions.SaveAsync("restart-account", new([new(".ROBLOSECURITY", "restart-test-cookie", ".roblox.com", "/", null, true, true)], DateTimeOffset.UtcNow));
        var preferences = new AppPreferences { StoragePath = Path.Combine(folder, "preferences.json"), AutoCheckSessions = false, AnimationsEnabled = false };
        var vm = new AccountsViewModel(accounts, credentials, new SessionManager(sessions, accounts), new AccountActivityTracker(), new NoBrowser(), preferences, NullLogger<AccountsViewModel>.Instance, groupRepository: groups);
        await vm.LoadAsync();
        vm.PlaceId = "123456";
        vm.JobId = "f17e57b4-d4fe-4aba-83c4-1782295a25a9";
        vm.SaveCurrentPlace();
        await vm.CreateGroupAsync("Builders");
        await vm.MoveToGroupAsync(vm.Groups.Single().Id, ["restart-account"]);
        if (vm.Accounts.Count != 1 || vm.Selected is null)
            throw new Exception("Grouped account disappeared");
        await vm.ToggleFavoriteAsync();
        vm.FilterMode = "Favorites";
        if (vm.Count != 1)
            throw new Exception("Favorite filter lost its account");
        await vm.ShutdownAsync();
        var backupPath = Path.Combine(folder, "backup.db");
        await db.BackupAsync(backupPath);
        if ((await new AccountRepository(new HiveDatabase(backupPath)).GetAllAsync()).Count != 1)
            throw new Exception("WAL backup lost committed accounts");
        var log = new NetworkLog();
        for (var i = 0; i < 300; i++)
            log.Record(new(DateTimeOffset.UtcNow, "users.roblox.com", "Session check", 200, i));
        if (log.Snapshot().Count != 250 || log.Snapshot()[0].Milliseconds != 299)
            throw new Exception("Network log retention or order failed");
        var process = new Process { StartInfo = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        process.StartInfo.ArgumentList.Add(typeof(PersistenceChecks).Assembly.Location);
        process.StartInfo.ArgumentList.Add("--verify-persistence");
        process.StartInfo.ArgumentList.Add(folder);
        process.Start();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        if (process.ExitCode != 0)
            throw new Exception("Independent restart check failed: " + await process.StandardError.ReadToEndAsync());
        await groups.DeleteAsync(vm.Groups.Single().Id);
        if ((await accounts.GetAllAsync()).Single().GroupId is not null)
            throw new Exception("Deleting a group did not preserve the account");
        Console.WriteLine("PASS: independent-process account/session/password/group/favorite/place persistence, selected startup account, saved places and group deletion.");
    }
    public static async Task VerifyChildAsync(string folder)
    {
        var db = new HiveDatabase(Path.Combine(folder, "hive.db"));
        var accounts = new AccountRepository(db);
        var account = (await accounts.GetAllAsync()).Single();
        var prefs = AppPreferences.Load(Path.Combine(folder, "preferences.json"));
        if (account.Username != "RestartUser" || account.Notes != "Keep description" || account.GroupId is null || prefs.PlaceId != "123456" || prefs.JobId.Length != 36 || prefs.FavoriteAccounts.Single() != account.Id || prefs.SavedPlaces.Count != 1)
            throw new Exception("Restart lost account metadata or preferences");
        if (await new CredentialStore(db, new DpapiSecretProtector()).ReadPasswordAsync(account.Id) != "restart-test-password" || (await new SessionStore(db, new DpapiSecretProtector()).ReadAsync(account.Id))?.Cookies.Single().Value != "restart-test-cookie")
            throw new Exception("Restart lost encrypted secrets");
        var vm = new AccountsViewModel(accounts, new CredentialStore(db, new DpapiSecretProtector()), new SessionManager(new SessionStore(db, new DpapiSecretProtector()), accounts), new AccountActivityTracker(), new NoBrowser(), prefs, NullLogger<AccountsViewModel>.Instance);
        await vm.LoadAsync();
        if (vm.Count != 1 || vm.Selected is null || vm.PlaceId != "123456")
            throw new Exception("Restart did not load the account list or place");
        await vm.ShutdownAsync();
    }
    private sealed class NoBrowser : IBrowserProvider
    {
        public Task<IBrowserSession> OpenAsync(BrowserProfile profile, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Offline check");
    }
}
