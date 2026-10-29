using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using Hive.Core;
using Hive.Infrastructure;
using Hive.Roblox;

internal static class AdvancedLaunchChecks
{
    public static async Task RunAsync()
    {
        var policy = new Policy { MaxParallelLaunches = 2 };
        var tracker = new Tracker(); var dispatcher = new BlockingDispatcher(tracker);
        await using (var manager = new InstanceManager(dispatcher, tracker, null, policy, TimeSpan.FromMilliseconds(10)))
        {
            foreach (var id in new[] { "a", "b", "c", "d" }) await manager.EnqueueAsync(new(id, 123, null));
            await Until(() => dispatcher.Starts == 2);
            if (dispatcher.MaxActive != 2 || manager.Instances.Count(x => x.State == InstanceState.Queued) != 2) throw new Exception("Configured concurrency or queued state is incorrect");
            manager.Cancel("c"); dispatcher.Release.TrySetResult();
            await Until(() => manager.Instances.Count(x => x.State == InstanceState.Running) == 3);
            if (dispatcher.Starts != 3 || dispatcher.MaxActive > 2 || manager.Instances.Single(x => x.AccountId == "c").State != InstanceState.Exited) throw new Exception("Cancelled queue entry launched or concurrency exceeded its limit");
        }
        policy.AutoRejoin = true; policy.MaxRejoinAttempts = 2; policy.RejoinDelaySeconds = 0.02;
        tracker = new Tracker(); var immediate = new ImmediateDispatcher(tracker);
        await using (var manager = new InstanceManager(immediate, tracker, TimeSpan.Zero, policy, TimeSpan.FromMilliseconds(10)))
        {
            await manager.EnqueueAsync(new("rejoin",123,null)); await Until(() => manager.Instances.Single().State == InstanceState.Running);
            tracker.Detach("rejoin"); await Until(() => immediate.Starts == 2 && manager.Instances.Single().State == InstanceState.Running);
            if (manager.Instances.Single().RejoinAttempts != 1) throw new Exception("Rejoin attempt count lost");
            tracker.Detach("rejoin"); await Until(() => immediate.Starts == 3 && manager.Instances.Single().State == InstanceState.Running);
            tracker.Detach("rejoin"); _ = manager.Instances; await Task.Delay(120);
            if (immediate.Starts != 3 || manager.Instances.Single().State != InstanceState.Exited) throw new Exception("Rejoin exceeded its attempt limit");
        }
        policy.RejoinDelaySeconds = 0.2; tracker = new Tracker(); immediate = new ImmediateDispatcher(tracker);
        await using (var manager = new InstanceManager(immediate, tracker, TimeSpan.Zero, policy, TimeSpan.FromMilliseconds(10)))
        {
            await manager.EnqueueAsync(new("cancel",123,null)); await Until(() => manager.Instances.Single().State == InstanceState.Running);
            tracker.Detach("cancel"); await Until(() => manager.Instances.Single().State == InstanceState.Backoff); manager.Cancel("cancel"); await Task.Delay(260);
            if (immediate.Starts != 1) throw new Exception("Cancelled backoff still launched");
            await manager.EnqueueAsync(new("cancel",123,null)); await Until(() => immediate.Starts == 2);
        }
        var denied = new DeniedDispatcher();
        await using (var manager = new InstanceManager(denied,new Tracker(),TimeSpan.Zero,policy,TimeSpan.FromMilliseconds(10)))
        {
            await manager.EnqueueAsync(new("denied",123,null)); await Until(() => manager.Instances.Single().State == InstanceState.Failed); await Task.Delay(250);
            if (denied.Starts != 1) throw new Exception("Authentication failure was retried automatically");
        }
        var suffix = Guid.NewGuid().ToString("N"); var mutexName = "Local\\Hive-test-mutex-" + suffix; var eventName = "Local\\Hive-test-event-" + suffix;
        using (var coordinator = new MultiInstanceCoordinator(() => false,mutexName,eventName))
        {
            coordinator.Prepare(); coordinator.Prepare(); using var contender = Mutex.OpenExisting(mutexName);
            if (contender.WaitOne(0)) { contender.ReleaseMutex(); throw new Exception("Multi-instance coordinator did not own its mutex"); }
            coordinator.Dispose(); if (!contender.WaitOne(1000)) throw new Exception("Multi-instance mutex was not released"); contender.ReleaseMutex();
        }
        using (var blocked = new MultiInstanceCoordinator(() => true,mutexName,eventName))
        {
            try { blocked.Prepare(); throw new Exception("Multi-instance enabled after clients were already running"); } catch (RobloxLaunchException) { }
        }
        var request = LaunchTarget.Parse("account",LaunchKind.PrivateServer,"", "https://www.roblox.com/games/123/example?privateServerLinkCode=private%2Bcode");
        var protocol = Uri.UnescapeDataString(LaunchProtocol.Create(request,"ticket","123456789",DateTimeOffset.UtcNow).ProtocolUri);
        if (request.PlaceId != 123 || !protocol.Contains("RequestPrivateGame") || !protocol.Contains("linkCode=private%2Bcode") || request.ToString().Contains("private+code")) throw new Exception("Private-server parsing, encoding or redaction failed");
        var follow = LaunchTarget.Parse("account",LaunchKind.FollowUser,"456", "");
        if (!Uri.UnescapeDataString(LaunchProtocol.Create(follow,"ticket","123456789",DateTimeOffset.UtcNow).ProtocolUri).Contains("RequestFollowUser&userId=456")) throw new Exception("Follow-user target failed");
        var share = LaunchTarget.Parse("account",LaunchKind.PrivateServer,"", "https://www.roblox.com/share?code=test&type=Server");
        try { LaunchProtocol.Create(share,"ticket","123456789",DateTimeOffset.UtcNow); throw new Exception("Unresolved share link reached protocol creation"); } catch (ArgumentException) { }
        try { LaunchTarget.Parse("account",LaunchKind.PrivateServer,"", "https://user:secret@www.roblox.com/share?code=test"); throw new Exception("Invite URL credentials accepted"); } catch (FormatException) { }
        using var client = new HttpClient(new RedirectHandler(false)); var resolved = await PrivateInviteResolver.ResolveAsync(share,client,default);
        if (resolved.PlaceId != 123 || resolved.LinkCode != "invite-code" || resolved.PrivateInviteUrl is not null) throw new Exception("Share-link resolution failed");
        using var external = new HttpClient(new RedirectHandler(true));
        try { await PrivateInviteResolver.ResolveAsync(share,external,default); throw new Exception("External invite redirect accepted"); } catch (RobloxLaunchException) { }
        Console.WriteLine("PASS: parallel launch limit, queue cancellation, rejoin/backoff/attempt cap, cancellation reset, auth failure stop, mutex ownership/release, private/follow/share targets and redaction.");
    }
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(4); while (!condition()) { if (DateTimeOffset.UtcNow > deadline) throw new Exception("Timed out waiting for advanced launch state"); await Task.Delay(5); }
    }
    private sealed class Policy : ILaunchPolicy
    {
        public int MaxParallelLaunches { get; set; } = 1; public double LaunchDelaySeconds { get; set; }
        public bool AutoRejoin { get; set; } public int MaxRejoinAttempts { get; set; } = 2;
        public double RejoinDelaySeconds { get; set; } = 0.02; public bool MultiInstance => false;
    }
    private sealed class Tracker : IAccountActivityTracker
    {
        private readonly ConcurrentDictionary<string,int> live = []; public void Attach(string id,int pid) => live[id] = pid;
        public int? GetLiveProcessId(string id) => live.TryGetValue(id,out var pid) ? pid : null; public void Detach(string id) => live.TryRemove(id,out _);
    }
    private sealed class BlockingDispatcher(Tracker tracker) : ILaunchDispatcher
    {
        private int starts, active; public int Starts => Volatile.Read(ref starts); public int MaxActive { get; private set; }
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<int?> DispatchAsync(LaunchRequest request,CancellationToken ct)
        {
            Interlocked.Increment(ref starts); var count = Interlocked.Increment(ref active); MaxActive = Math.Max(MaxActive,count);
            try { await Release.Task.WaitAsync(ct); tracker.Attach(request.AccountId,Starts+100); return tracker.GetLiveProcessId(request.AccountId); } finally { Interlocked.Decrement(ref active); }
        }
    }
    private sealed class ImmediateDispatcher(Tracker tracker) : ILaunchDispatcher
    {
        public int Starts; public Task<int?> DispatchAsync(LaunchRequest request,CancellationToken ct) { var pid = Interlocked.Increment(ref Starts)+100; tracker.Attach(request.AccountId,pid); return Task.FromResult<int?>(pid); }
    }
    private sealed class DeniedDispatcher : ILaunchDispatcher
    {
        public int Starts; public Task<int?> DispatchAsync(LaunchRequest request,CancellationToken ct) { Interlocked.Increment(ref Starts); throw new RobloxLaunchException("Sign in again."); }
    }
    private sealed class RedirectHandler(bool external) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri(external ? "https://example.com/steal" : "https://www.roblox.com/games/123/test?privateServerLinkCode=invite-code"); return Task.FromResult(response);
        }
    }
}
