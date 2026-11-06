using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Hive.Core;
using Hive.Infrastructure;
using Hive.Roblox;
using Hive.Storage;

internal static class FeatureChecks
{
    public static async Task RunAsync(HiveDatabase database, AccountRepository accounts, SessionStore sessions)
    {
        var username = "BrowserAdded";
        var verifiedStore = new VerifiedAccountStore(database, new DpapiSecretProtector());
        var onboarding = new AccountOnboardingService(accounts, sessions, jar => new Handler(_ =>
        {
            jar.Add(new Cookie(".ROBLOSECURITY", "identified-replacement", "/", ".roblox.com") { HttpOnly = true, Secure = true });
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    id = 1001,
                    name = username,
                    displayName = "Browser account"
                }))
            };
        }), verifiedStore: verifiedStore);
        var session = new AccountSession([new(".ROBLOSECURITY", "browser-session", ".roblox.com", "/", null, true, true)], DateTimeOffset.UtcNow);
        var added = await onboarding.AddAsync(session) ?? throw new Exception("Browser onboarding failed");
        if (added.RobloxUserId != 1001 || added.SessionState != SessionState.Valid || (await sessions.ReadAsync(added.Id))!.Cookies.Single().Value != "identified-replacement")
            throw new Exception("Onboarding identity or rotation was lost");
        await accounts.SaveAsync(added with
        {
            Notes = "Keep this note",
            DisplayName = "My alias"
        });
        username = "RenamedBrowserUser";
        var updated = await onboarding.AddAsync(session) ?? throw new Exception("Re-onboarding failed");
        if (updated.Id != added.Id || updated.Notes != "Keep this note" || updated.DisplayName != "My alias" || (await accounts.GetAllAsync()).Count != 1)
            throw new Exception("Stable user identity failed to deduplicate");
        await using (var connection = await database.OpenAsync())
        {
            using var trigger = connection.CreateCommand(); trigger.CommandText = "CREATE TRIGGER test_reject_session_insert BEFORE INSERT ON Sessions BEGIN SELECT RAISE(ABORT, 'test failure'); END"; await trigger.ExecuteNonQueryAsync();
        }
        try
        {
            try { await verifiedStore.SaveAsync(new RobloxIdentity(7001, "RollbackOnboard", "Test"), session); throw new Exception("Expected onboarding transaction failure"); }
            catch (Microsoft.Data.Sqlite.SqliteException) { }
            if ((await accounts.GetAllAsync()).Any(a => a.Username == "RollbackOnboard")) throw new Exception("Failed onboarding left a phantom account");
        }
        finally
        {
            await using var connection = await database.OpenAsync(); using var trigger = connection.CreateCommand(); trigger.CommandText = "DROP TRIGGER test_reject_session_insert"; await trigger.ExecuteNonQueryAsync();
        }
        var rejected = new AccountOnboardingService(accounts, sessions, _ => new Handler(_ => new(HttpStatusCode.Unauthorized)));
        if (await rejected.AddAsync(session) != null || (await accounts.GetAllAsync()).Count != 1)
            throw new Exception("Rejected session was imported");
        var input = """
  [{"Username":"ImportedUser","Alias":"Imported alias","Description":"Imported note","Password":"import-private-password","SecurityToken":"import-private-cookie","Group":"EXISTING","Fields":{"Custom":"private-field-value"}},
   {"Username":"importeduser","Description":"Duplicate"},
   {"Username":"RenamedBrowserUser","Description":"Do not overwrite"}]
  """;
        var parsed = RamImportService.Parse(input);
        var groups = new AccountGroupRepository(database);
        await groups.SaveAsync(new("existing-group", "Existing"));
        var importer = new RamImportService(database, new DpapiSecretProtector());
        var summary = await importer.ImportAsync(parsed);
        if (summary.Added != 1 || summary.Skipped != 1)
            throw new Exception("RAM duplicate handling failed");
        var imported = (await accounts.GetAllAsync()).Single(a => a.Username == "ImportedUser");
        var fieldsStore = new AccountFieldsStore(database, new DpapiSecretProtector());
        if ((await fieldsStore.ReadAsync(imported.Id))["Custom"] != "private-field-value") throw new Exception("RAM custom fields lost");
        await fieldsStore.SaveAsync(imported.Id, new Dictionary<string, string> { ["Edited"] = "private-edited-value" });
        if ((await fieldsStore.ReadAsync(imported.Id))["Edited"] != "private-edited-value") throw new Exception("Custom fields did not persist");
        await using (var conn = await database.OpenAsync())
        {
            using var command = conn.CreateCommand(); command.CommandText = "SELECT Payload FROM AccountFields WHERE AccountId=$id"; command.Parameters.AddWithValue("$id", imported.Id);
            var payload = (byte[])(await command.ExecuteScalarAsync())!;
            if (System.Text.Encoding.UTF8.GetString(payload).Contains("private-edited-value")) throw new Exception("Custom field stored in plaintext");
        }
        var credentials = new CredentialStore(database, new DpapiSecretProtector());
        if (imported.Notes != "Imported note" || imported.DisplayName != "Imported alias" || imported.GroupId != "existing-group" || imported.SessionState != SessionState.Unknown || await credentials.ReadPasswordAsync(imported.Id) != "import-private-password" || (await sessions.ReadAsync(imported.Id))!.Cookies.Single().Value != "import-private-cookie")
            throw new Exception("RAM fields or encrypted secrets lost");
        var groupItems = RamImportService.Parse("[{\"Username\":\"GroupOne\",\"Group\":\"Friends\"},{\"Username\":\"GroupTwo\",\"Group\":\"friends\"},{\"Username\":\"DefaultUser\",\"Group\":\"Default\"}]");
        await importer.ImportAsync(groupItems);
        var importedGroups = await groups.GetAllAsync();
        var friendGroup = importedGroups.Single(g => g.Name == "Friends");
        var groupAccounts = await accounts.GetAllAsync();
        if (groupAccounts.Single(a => a.Username == "GroupOne").GroupId != friendGroup.Id || groupAccounts.Single(a => a.Username == "GroupTwo").GroupId != friendGroup.Id || groupAccounts.Single(a => a.Username == "DefaultUser").GroupId is not null) throw new Exception("RAM groups were not preserved or default was duplicated");
        if ((await accounts.GetAllAsync()).Single(a => a.Id == added.Id).Notes != "Keep this note")
            throw new Exception("RAM import overwrote an existing account");
        var before = (await accounts.GetAllAsync()).Count;
        var failing = new RamImportService(database, new FailingProtector());
        try
        {
            await failing.ImportAsync([new("RollbackUser", "", "", "test-password", null, "RollbackGroup")]);
            throw new Exception("Expected import storage failure");
        }
        catch (IOException) { }
        if ((await accounts.GetAllAsync()).Count != before || (await groups.GetAllAsync()).Any(g => g.Name == "RollbackGroup"))
            throw new Exception("RAM import did not roll back on secret protection failure");
        try
        {
            RamImportService.Parse("[\"bad entry\"]");
            throw new Exception("Invalid RAM structure accepted");
        }
        catch (FormatException) { }
        foreach (var account in await accounts.GetAllAsync())
            await accounts.DeleteAsync(account.Id);
        if ((await fieldsStore.ReadAsync(imported.Id)).Count != 0) throw new Exception("Custom fields did not cascade on account deletion");
        foreach (var group in await groups.GetAllAsync()) await groups.DeleteAsync(group.Id);
        Console.WriteLine("PASS: browser identity discovery, stable-ID deduplication, rotated token capture, rejected login, RAM preview parsing, aliases/notes, encrypted import, skip-existing and transaction rollback.");
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }
    private sealed class FailingProtector : ISecretProtector
    {
        public byte[] Protect(byte[] data) => throw new IOException("Simulated protection failure");
        public byte[] Unprotect(byte[] data) => throw new NotSupportedException();
    }
}
