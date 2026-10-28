using System.IO;
using Hive.Core;
using Hive.App;
using Hive.Infrastructure;
using Hive.Storage;
using Hive.Roblox;
using Microsoft.Extensions.Logging.Abstractions;
internal static class PreviewFixtures
{
    public static AccountsViewModel CreateViewModel(string? preferencesPath = null)
    {
        // Visual fixtures exist only in this test process and are never written to the user's database.
        var repository = new FixtureRepository();
        var database = new HiveDatabase(Path.Combine(Path.GetTempPath(), "Hive-unused-preview.db"));
        var preferences = new AppPreferences { AutoCheckSessions = false, AnimationsEnabled = false, StoragePath = preferencesPath };
        var vm = new AccountsViewModel(repository, new FixtureCredentials(), new SessionManager(new SessionStore(database, new DpapiSecretProtector()), repository), new FixtureTracker(), new ConfiguredBrowserProvider(preferences, ""), preferences, NullLogger<AccountsViewModel>.Instance);
        vm.LoadAsync().GetAwaiter().GetResult();
        vm.Selected = vm.Accounts[0];
        return vm;
    }
    private sealed class FixtureRepository : IAccountRepository
    {
        public Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Account>>([
         new("preview-a","HoneyExplorer","",null,"Main account",SessionState.Valid),
   new("preview-b","AmberBuilder","",null,"Building and testing",SessionState.Valid),
   new("preview-c","QuietBee","",null,"Backup account",SessionState.Unknown),
   new("preview-d","OldHive","",null,"Sign in to restore access",SessionState.ReauthenticationRequired)]);
        public Task SaveAsync(Account account, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetSessionStateAsync(string id, SessionState state, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class FixtureCredentials : ICredentialStore
    {
        public Task<bool> HasPasswordAsync(string accountId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<string?> ReadPasswordAsync(string accountId, CancellationToken ct = default) => Task.FromResult<string?>("preview-password");
        public Task SavePasswordAsync(string accountId, string password, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class FixtureTracker : IAccountActivityTracker
    {
        public void Attach(string accountId, int processId)
        {
        }
        public int? GetLiveProcessId(string accountId) => accountId == "preview-a" ? 12480 : accountId == "preview-b" ? 12912 : null;
        public void Detach(string accountId)
        {
        }
    }
}
