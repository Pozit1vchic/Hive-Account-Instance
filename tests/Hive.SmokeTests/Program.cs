using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hive.Core;
using Hive.Storage;
using Hive.Infrastructure;
using Hive.App;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using Dapper;

if (args.Length == 2 && args[0] == "--verify-persistence")
{
    await PersistenceChecks.VerifyChildAsync(args[1]);
    return;
}
var folder = Path.Combine(Path.GetTempPath(), "Hive-Smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var database = new HiveDatabase(Path.Combine(folder, "test.db"));
    await database.InitializeAsync();
    await database.InitializeAsync();
    var accounts = new AccountRepository(database);
    var account = new Account(Guid.NewGuid().ToString("N"), "hive_test", "Hive Test", null, "Local note", SessionState.ReauthenticationRequired);
    await accounts.SaveAsync(account);
    if ((await accounts.GetAllAsync()).Single() != account)
        throw new Exception("Account roundtrip failed");
    await accounts.SaveAsync(account with
    {
        Notes = "Edited"
    });
    if ((await accounts.GetAllAsync()).Single().Notes != "Edited")
        throw new Exception("Update failed");
    try
    {
        await accounts.SaveAsync(account with
        {
            Id = "duplicate",
            Username = "HIVE_TEST"
        });
        throw new Exception("Duplicate accepted");
    }
    catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { }
    var sessions = new SessionStore(database, new DpapiSecretProtector());
    const string secret = "smoke-test-session-secret";
    await sessions.SaveAsync(account.Id, new AccountSession([new SessionCookie("test", secret, ".roblox.com", "/", null, true, true)], DateTimeOffset.UtcNow));
    if ((await sessions.ReadAsync(account.Id))?.Cookies.Single().Value != secret)
        throw new Exception("DPAPI roundtrip failed");
    await using (var c = await database.OpenAsync())
    {
        var bytes = await c.QuerySingleAsync<byte[]>("SELECT Payload FROM Sessions");
        if (Encoding.UTF8.GetString(bytes).Contains(secret))
            throw new Exception("Session stored in plaintext");
    }
    var vm = new AccountsViewModel(accounts, new CredentialStore(database, new DpapiSecretProtector()), new Hive.Roblox.SessionManager(sessions, accounts), new AccountActivityTracker(), new Hive.App.ConfiguredBrowserProvider(new AppPreferences(), ""), new AppPreferences { AutoCheckSessions = false }, NullLogger<AccountsViewModel>.Instance);
    await vm.LoadAsync();
    vm.Search = "missing";
    if (vm.Count != 0)
        throw new Exception("Search failed");
    vm.Search = "hive";
    if (vm.Count != 1)
        throw new Exception("Search failed");
    await accounts.DeleteAsync(account.Id);
    if ((await accounts.GetAllAsync()).Count != 0 || await sessions.ReadAsync(account.Id) != null)
        throw new Exception("Cascade failed");
    Console.WriteLine("PASS: migrations, CRUD, uniqueness, search, DPAPI encryption, session roundtrip, cascade deletion.");
    await SecurityChecks.RunAsync(database, accounts, sessions, folder);
    await FeatureChecks.RunAsync(database, accounts, sessions);
    await PersistenceChecks.RunAsync(folder);
    await BrowserFlowChecks.RunAsync(database, accounts, sessions, folder);
    await LaunchChecks.RunAsync(accounts, sessions);
    Exception? renderError = null;
    var thread = new Thread(() => { try { VisualChecks.Run(folder); } catch (Exception ex) { renderError = ex; } });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (renderError is not null)
        throw renderError;

}
finally { SqliteConnection.ClearAllPools(); Directory.Delete(folder, true); }
