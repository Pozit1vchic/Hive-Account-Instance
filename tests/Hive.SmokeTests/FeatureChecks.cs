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
        }));
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
        var rejected = new AccountOnboardingService(accounts, sessions, _ => new Handler(_ => new(HttpStatusCode.Unauthorized)));
        if (await rejected.AddAsync(session) != null || (await accounts.GetAllAsync()).Count != 1)
            throw new Exception("Rejected session was imported");
        var input = """
  [{"Username":"ImportedUser","Alias":"Imported alias","Description":"Imported note","Password":"import-private-password","SecurityToken":"import-private-cookie"},
   {"Username":"importeduser","Description":"Duplicate"},
   {"Username":"RenamedBrowserUser","Description":"Do not overwrite"}]
  """;
        var parsed = RamImportService.Parse(input);
        var importer = new RamImportService(database, new DpapiSecretProtector());
        var summary = await importer.ImportAsync(parsed);
        if (summary.Added != 1 || summary.Skipped != 1)
            throw new Exception("RAM duplicate handling failed");
        var imported = (await accounts.GetAllAsync()).Single(a => a.Username == "ImportedUser");
        var credentials = new CredentialStore(database, new DpapiSecretProtector());
        if (imported.Notes != "Imported note" || imported.DisplayName != "Imported alias" || imported.SessionState != SessionState.Unknown || await credentials.ReadPasswordAsync(imported.Id) != "import-private-password" || (await sessions.ReadAsync(imported.Id))!.Cookies.Single().Value != "import-private-cookie")
            throw new Exception("RAM fields or encrypted secrets lost");
        if ((await accounts.GetAllAsync()).Single(a => a.Id == added.Id).Notes != "Keep this note")
            throw new Exception("RAM import overwrote an existing account");
        var before = (await accounts.GetAllAsync()).Count;
        var failing = new RamImportService(database, new FailingProtector());
        try
        {
            await failing.ImportAsync([new("RollbackUser", "", "", "test-password", null)]);
            throw new Exception("Expected import storage failure");
        }
        catch (IOException) { }
        if ((await accounts.GetAllAsync()).Count != before)
            throw new Exception("RAM import did not roll back on secret protection failure");
        try
        {
            RamImportService.Parse("[\"bad entry\"]");
            throw new Exception("Invalid RAM structure accepted");
        }
        catch (FormatException) { }
        foreach (var account in await accounts.GetAllAsync())
            await accounts.DeleteAsync(account.Id);
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
