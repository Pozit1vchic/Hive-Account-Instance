using System.Net.Http;
using System.IO;
using System.Net;
using System.Text;
using Dapper;
using Hive.Core;
using Hive.Storage;
using Hive.Infrastructure;
using Hive.Roblox;
using Hive.App;
using Microsoft.Extensions.Logging.Abstractions;
internal static class SecurityChecks
{
    public static async Task RunAsync(HiveDatabase db, AccountRepository accounts, SessionStore sessions, string folder)
    {
        var account = new Account("security", "hive_security", "", null, "", SessionState.Unknown);
        await accounts.SaveAsync(account);
        var credentials = new CredentialStore(db, new DpapiSecretProtector());
        const string password = "private-password-smoke-123";
        await credentials.SavePasswordAsync(account.Id, password);
        if (!await credentials.HasPasswordAsync(account.Id) || await credentials.ReadPasswordAsync(account.Id) != password)
            throw new Exception("Credential roundtrip");
        await using (var c = await db.OpenAsync())
        {
            var bytes = await c.QuerySingleAsync<byte[]>("SELECT Payload FROM Credentials WHERE AccountId='security'");
            if (Encoding.UTF8.GetString(bytes).Contains(password))
                throw new Exception("Plaintext password");
        }
        var initial = new AccountSession([new(".ROBLOSECURITY", "initial-token", ".roblox.com", "/", null, true, true)], DateTimeOffset.UtcNow);
        await sessions.SaveAsync(account.Id, initial);
        SessionManager Manager(HttpStatusCode code, string json, bool rotate = false) => new(sessions, accounts, jar => new ResponseHandler(_ =>
        {
            if (rotate)
                jar.Add(new Cookie(".ROBLOSECURITY", "rotated-token", "/", ".roblox.com") { Secure = true, HttpOnly = true });
            return new HttpResponseMessage(code) { Content = new StringContent(json) };
        }));
        var valid = await Manager(HttpStatusCode.OK, "{\"name\":\"hive_security\"}", true).CheckAsync(account.Id);
        if (valid.State != SessionState.Valid || !valid.Confirmed || (await sessions.ReadAsync(account.Id))!.Cookies.Single(c => c.Name == ".ROBLOSECURITY").Value != "rotated-token")
            throw new Exception("Validation or server cookie rotation failed");
        foreach (var code in new[] { HttpStatusCode.TooManyRequests, HttpStatusCode.Forbidden, HttpStatusCode.InternalServerError })
        {
            var transient = await Manager(code, "{}").CheckAsync(account.Id);
            if (transient.Confirmed || (await accounts.GetAllAsync()).Single().SessionState != SessionState.Valid)
                throw new Exception("Transient response invalidated session");
        }
        var timeoutManager = new SessionManager(sessions, accounts, _ => new ResponseHandler(_ => throw new TaskCanceledException()));
        if ((await timeoutManager.CheckAsync(account.Id)).Confirmed)
            throw new Exception("Timeout invalidated session");
        var foreign = await Manager(HttpStatusCode.OK, "{\"name\":\"someone_else\"}").ImportAsync(account, initial);
        if (foreign.Confirmed || (await sessions.ReadAsync(account.Id))!.Cookies.Single(c => c.Name == ".ROBLOSECURITY").Value != "rotated-token")
            throw new Exception("Foreign identity replaced existing session");
        var invalid = await Manager(HttpStatusCode.Unauthorized, "{}").CheckAsync(account.Id);
        if (!invalid.Confirmed || invalid.State != SessionState.ReauthenticationRequired || (await accounts.GetAllAsync()).Single().SessionState != SessionState.ReauthenticationRequired)
            throw new Exception("Invalid session not persisted");
        var aged = initial with
        {
            SavedAt = DateTimeOffset.UtcNow.AddDays(-2),
            LastRenewedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        await sessions.SaveAsync(account.Id, aged);
        var refreshRequests = 0;
        var renewal = new SessionManager(sessions, accounts, jar => new ResponseHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"name\":\"hive_security\"}")
                };
            refreshRequests++;
            if (refreshRequests == 1)
            {
                var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden);
                challenge.Headers.Add("x-csrf-token", "csrf-smoke");
                return challenge;
            }
            if (!request.Headers.TryGetValues("x-csrf-token", out var csrf) || csrf.Single() != "csrf-smoke")
                throw new Exception("Refresh CSRF retry missing");
            jar.Add(new Cookie(".ROBLOSECURITY", "renewed-token", "/", ".roblox.com") { Secure = true, HttpOnly = true });
            return new(HttpStatusCode.OK);
        }));
        var renewed = await renewal.CheckAsync(account.Id);
        var renewedSession = (await sessions.ReadAsync(account.Id))!;
        if (!renewed.Confirmed || renewed.State != SessionState.Valid || refreshRequests != 2 || renewedSession.Cookies.Single(c => c.Name == ".ROBLOSECURITY").Value != "renewed-token" || renewedSession.LastRenewedAt < DateTimeOffset.UtcNow.AddMinutes(-1))
            throw new Exception("Automatic refresh failed");
        await renewal.CheckAsync(account.Id);
        if (refreshRequests != 2)
            throw new Exception("Session renewed too frequently");
        await sessions.SaveAsync(account.Id, aged);
        var rateLimitedRequests = 0;
        var limited = new SessionManager(sessions, accounts, _ => new ResponseHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"name\":\"hive_security\"}")
                };
            rateLimitedRequests++;
            return new(HttpStatusCode.TooManyRequests);
        }));
        await limited.CheckAsync(account.Id);
        await limited.CheckAsync(account.Id);
        if (rateLimitedRequests != 1 || (await accounts.GetAllAsync()).Single().SessionState != SessionState.Valid)
            throw new Exception("Refresh rate-limit backoff failed");
        await sessions.SaveAsync(account.Id, aged);
        var disabled = new SessionManager(sessions, accounts, _ => new ResponseHandler(request =>
        {
            if (request.Method != HttpMethod.Get)
                throw new Exception("Refresh setting ignored");
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"name\":\"hive_security\"}")
            };
        }));
        await disabled.CheckAsync(account.Id, renewIfDue: false);
        Console.WriteLine("PASS: automatic session refresh, CSRF retry, renewed cookie persistence, refresh interval, rate-limit backoff and disable setting.");
        var tracker = new AccountActivityTracker();
        try
        {
            tracker.Attach(account.Id, Environment.ProcessId);
            throw new Exception("Accepted a non-Roblox process");
        }
        catch (ProcessAssignmentException) { }
        if (tracker.GetLiveProcessId(account.Id) != null)
            throw new Exception("Invented running account");
        ProcessSnapshot? observed = new("RobloxPlayerBeta", DateTime.UtcNow);
        var tracked = new AccountActivityTracker(_ => observed);
        tracked.Attach("a", 123);
        if (tracked.GetLiveProcessId("a") != 123)
            throw new Exception("Live process not tracked");
        try
        {
            tracked.Attach("b", 123);
            throw new Exception("PID assigned to two accounts");
        }
        catch (ProcessAssignmentException) { }
        observed = observed with
        {
            StartedUtc = observed.StartedUtc.AddSeconds(1)
        };
        if (tracked.GetLiveProcessId("a") != null)
            throw new Exception("Reused PID retained old account");
        tracked.Attach("a", 123);
        observed = null;
        if (tracked.GetLiveProcessId("a") != null)
            throw new Exception("Exited process still running");
        var preferences = new AppPreferences { AutoCheckSessions = false, StoragePath = Path.Combine(folder, "preferences.json") };
        var vm = new AccountsViewModel(accounts, credentials, new SessionManager(sessions, accounts), tracker, new ConfiguredBrowserProvider(preferences, ""), preferences, NullLogger<AccountsViewModel>.Instance);
        await vm.LoadAsync();
        if (vm.Accounts.Single().PasswordText == password)
            throw new Exception("Password revealed by default");
        vm.ShowPasswords = true;
        await vm.RefreshPasswordsAsync();
        if (vm.Accounts.Single().PasswordText != password)
            throw new Exception("Reveal setting failed");
        vm.ShowPasswords = false;
        await vm.RefreshPasswordsAsync();
        if (vm.Accounts.Single().PasswordText != "••••••••")
            throw new Exception("Remasking failed");
        await accounts.DeleteAsync(account.Id);
        if (await credentials.HasPasswordAsync(account.Id) || await sessions.ReadAsync(account.Id) != null)
            throw new Exception("Secret cascade deletion failed");
        Console.WriteLine("PASS: encrypted credentials, masking/reveal, server cookie rotation, 401 state persistence, transient errors, identity mismatch rejection, non-Roblox/duplicate PID rejection, process exit and PID reuse, secret cascade deletion.");
    }
    private sealed class ResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.AbsoluteUri is not ("https://users.roblox.com/v1/users/authenticated" or "https://auth.roblox.com/v2/session/refresh"))
                throw new Exception("Unexpected auth destination");
            return Task.FromResult(respond(request));
        }
    }
}
