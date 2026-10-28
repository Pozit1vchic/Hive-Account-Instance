using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using Hive.Core;
using Hive.Infrastructure;
using Hive.Roblox;
using Hive.Storage;
internal static class LaunchChecks
{
    public static async Task RunAsync(AccountRepository accounts, SessionStore sessions)
    {
        var account = new Account("launch-test", "LaunchUser", "", null, "", SessionState.Unknown, 9001);
        await accounts.SaveAsync(account);
        await sessions.SaveAsync(account.Id, new([new(".ROBLOSECURITY", "launch-test-session", ".roblox.com", "/", null, true, true)], DateTimeOffset.UtcNow));
        var ticketCalls = 0;
        var broker = new SessionManager(sessions, accounts, _ => new Handler(request =>
        {
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"name\":\"LaunchUser\",\"id\":9001}")
                };
            if (request.RequestUri?.AbsoluteUri != "https://auth.roblox.com/v1/authentication-ticket/" || !request.Headers.Contains("RBXAuthenticationNegotiation"))
                throw new Exception("Unexpected ticket request");
            ticketCalls++;
            if (ticketCalls == 1)
            {
                var csrf = new HttpResponseMessage(HttpStatusCode.Forbidden);
                csrf.Headers.Add("x-csrf-token", "launch-csrf");
                return csrf;
            }
            if (!request.Headers.TryGetValues("x-csrf-token", out var value) || value.Single() != "launch-csrf")
                throw new Exception("Missing ticket CSRF retry");
            var success = new HttpResponseMessage(HttpStatusCode.OK);
            success.Headers.Add("rbx-authentication-ticket", "opaque+ticket:value");
            return success;
        }));
        var job = Guid.NewGuid().ToString();
        var spec = await broker.CreateLaunchAsync(new(account.Id, 123456, job));
        if (ticketCalls != 2 || !spec.ProtocolUri.StartsWith("roblox-player:1+") || !spec.ProtocolUri.Contains("gameinfo:opaque%2Bticket%3Avalue") || !Uri.UnescapeDataString(spec.ProtocolUri).Contains("gameId=" + job) || spec.ToString().Contains("opaque"))
            throw new Exception("Launch protocol encoding or secret redaction failed");
        try
        {
            await broker.CreateLaunchAsync(new(account.Id, 0, null));
            throw new Exception("Invalid Place ID accepted");
        }
        catch (RobloxLaunchException) { }
        var unauthorized = new SessionManager(sessions, accounts, _ => new Handler(request => request.Method == HttpMethod.Get ? new(HttpStatusCode.OK) { Content = new StringContent("{\"name\":\"LaunchUser\",\"id\":9001}") } : new(HttpStatusCode.Unauthorized)));
        try
        {
            await unauthorized.CreateLaunchAsync(new(account.Id, 123, null));
            throw new Exception("Unauthorized launch accepted");
        }
        catch (RobloxLaunchException) { }
        if ((await accounts.GetAllAsync()).Single().SessionState != SessionState.ReauthenticationRequired)
            throw new Exception("Unauthorized ticket did not invalidate the session");
        await accounts.DeleteAsync(account.Id);
        var tracker = new Tracker();
        var dispatcher = new Dispatcher(tracker);
        await using (var manager = new InstanceManager(dispatcher, tracker, TimeSpan.Zero))
        {
            await manager.EnqueueAsync(new("a", 123, null));
            await dispatcher.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            try
            {
                await manager.EnqueueAsync(new("a", 123, null));
                throw new Exception("Duplicate queued launch accepted");
            }
            catch (RobloxLaunchException) { }
            await manager.EnqueueAsync(new("b", 123, null));
            if (manager.Instances.Single(i => i.AccountId == "b").State != InstanceState.Queued)
                throw new Exception("Launch was not queued");
            dispatcher.Release.TrySetResult();
            await Until(() => manager.Instances.Count(i => i.State == InstanceState.Running) == 2);
            if (dispatcher.MaxActive != 1)
                throw new Exception("Launch concurrency exceeded one");
            tracker.Detach("a");
            if (manager.Instances.Single(i => i.AccountId == "a").State != InstanceState.Exited)
                throw new Exception("Exited game still marked running");
        }
        await using (var unknown = new InstanceManager(new UnknownDispatcher(), new Tracker(), TimeSpan.Zero))
        {
            await unknown.EnqueueAsync(new("c", 123, null));
            await Until(() => unknown.Instances.Any(i => i.State == InstanceState.AwaitingProcess));
            if (unknown.Instances.Any(i => i.State == InstanceState.Running))
                throw new Exception("Unidentified process shown as running");
        }
        var credentials = AccountInputParser.Credentials("User:pass:with:colons\nuser:duplicate\nOther: password ");
        if (credentials.Count != 2 || credentials[0].Password != "pass:with:colons" || credentials[1].Password != " password " || credentials[0].ToString().Contains("pass"))
            throw new Exception("Credential parsing failed");
        if (AccountInputParser.Cookies(".ROBLOSECURITY=cookie-value;\nother-cookie")[0] != "cookie-value")
            throw new Exception("Cookie prefix parsing failed");
        Console.WriteLine("PASS: launch ticket CSRF/negotiation, protocol encoding, rejected launch state, serial launch queue, duplicate rejection, process exit/unconfirmed state, bounded credential parsing.");
    }
    private static async Task Until(Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow > end)
                throw new Exception("Timed out waiting for launch state");
            await Task.Delay(10);
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }
    private sealed class Tracker : IAccountActivityTracker
    {
        private readonly ConcurrentDictionary<string, int> live = []; public void Attach(string accountId, int processId) => live[accountId] = processId; public int? GetLiveProcessId(string accountId) => live.TryGetValue(accountId, out var pid) ? pid : null; public void Detach(string accountId) => live.TryRemove(accountId, out _);
    }
    private sealed class Dispatcher(Tracker tracker) : ILaunchDispatcher
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); private int active; public int MaxActive
        {
            get; private set;
        }
        public async Task<int?> DispatchAsync(LaunchRequest request, CancellationToken ct = default)
        {
            active++;
            MaxActive = Math.Max(MaxActive, active);
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
            var pid = request.AccountId == "a" ? 41 : 42;
            tracker.Attach(request.AccountId, pid);
            active--;
            return pid;
        }
    }
    private sealed class UnknownDispatcher : ILaunchDispatcher
    {
        public Task<int?> DispatchAsync(LaunchRequest request, CancellationToken ct = default) => Task.FromResult<int?>(null);
    }
}
