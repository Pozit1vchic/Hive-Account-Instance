using System.Collections.Concurrent;
using System.Threading.Channels;
using Hive.Core;
namespace Hive.Infrastructure;

public sealed class InstanceManager : IInstanceManager, IAsyncDisposable, IDisposable
{
    private sealed record Work(Guid Version, LaunchRequest Request, int Attempt, CancellationTokenSource Cancellation);
    private readonly ILaunchDispatcher dispatcher;
    private readonly IAccountActivityTracker tracker;
    private readonly ILaunchPolicy? policy;
    private readonly TimeSpan? fixedSpacing;
    private readonly TimeSpan watchInterval;
    private readonly Channel<Work> queue = Channel.CreateBounded<Work>(50);
    private readonly ConcurrentDictionary<string, GameInstance> states = [];
    private readonly ConcurrentDictionary<string, LaunchRequest> targets = [];
    private readonly ConcurrentDictionary<string, Work> pending = [];
    private readonly ConcurrentDictionary<string, Guid> versions = [];
    private readonly ConcurrentDictionary<string, byte> stopped = [];
    private readonly ConcurrentDictionary<string, DateTimeOffset> runningSince = [];
    private readonly ConcurrentDictionary<Guid, Task> active = [];
    private readonly object pendingSync = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task scheduler;
    private readonly Task watcher;

    public InstanceManager(ILaunchDispatcher dispatcher, IAccountActivityTracker tracker, TimeSpan? launchSpacing = null, ILaunchPolicy? policy = null, TimeSpan? watchInterval = null)
    {
        this.dispatcher = dispatcher; this.tracker = tracker; this.policy = policy; fixedSpacing = launchSpacing;
        this.watchInterval = watchInterval ?? TimeSpan.FromSeconds(1);
        if (launchSpacing < TimeSpan.Zero || this.watchInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(launchSpacing));
        scheduler = ScheduleAsync(); watcher = WatchAsync();
    }
    public IReadOnlyList<GameInstance> Instances { get { RefreshProcesses(); return states.Values.ToArray(); } }
    private bool Current(Work work) => versions.TryGetValue(work.Request.AccountId, out var id) && id == work.Version;
    private void State(Work work, InstanceState state, int? pid = null, string? detail = null)
    {
        if (Current(work)) states[work.Request.AccountId] = new(work.Request.AccountId, pid, state, work.Attempt, detail);
    }
    private Work NewWork(LaunchRequest request, int attempt)
    {
        var work = new Work(Guid.NewGuid(), request, attempt, CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token));
        lock (pendingSync)
        {
            if (pending.Count >= 50 || !pending.TryAdd(request.AccountId, work)) { work.Cancellation.Dispose(); throw new RobloxLaunchException("This account is already queued, or the queue is full."); }
            versions[request.AccountId] = work.Version;
        }
        return work;
    }
    private void Remove(Work work) => ((ICollection<KeyValuePair<string, Work>>)pending).Remove(new(work.Request.AccountId, work));
    public Task EnqueueAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (lifetime.IsCancellationRequested) throw new InvalidOperationException("Launch queue is closed.");
        if (tracker.GetLiveProcessId(request.AccountId).HasValue) throw new RobloxLaunchException("This account already has a linked Roblox process.");
        var work = NewWork(request, 0); stopped.TryRemove(request.AccountId, out _); targets[request.AccountId] = request;
        State(work, InstanceState.Queued);
        if (!queue.Writer.TryWrite(work)) { Remove(work); work.Cancellation.Dispose(); State(work, InstanceState.Failed, detail: "Launch queue is full."); throw new RobloxLaunchException("Launch queue is full."); }
        return Task.CompletedTask;
    }
    public void Cancel(string accountId)
    {
        stopped[accountId] = 0;
        if (pending.TryGetValue(accountId, out var work))
        {
            try { work.Cancellation.Cancel(); } catch (ObjectDisposedException) { }
            Remove(work); State(work, InstanceState.Exited, detail: "Launch cancelled.");
        }
    }
    private async Task ScheduleAsync()
    {
        var previous = DateTimeOffset.MinValue;
        try
        {
            await foreach (var work in queue.Reader.ReadAllAsync(lifetime.Token))
            {
                while (active.Count >= Math.Clamp(policy?.MaxParallelLaunches ?? 1, 1, 4))
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    var tasks = active.Values.ToArray();
                    if (tasks.Length > 0) await Task.WhenAny(Task.WhenAny(tasks), Task.Delay(50, lifetime.Token));
                }
                if (work.Cancellation.IsCancellationRequested || !Current(work)) { Remove(work); work.Cancellation.Dispose(); continue; }
                var spacing = fixedSpacing ?? TimeSpan.FromSeconds(Math.Clamp(policy?.LaunchDelaySeconds ?? 3, 0, 30));
                var wait = spacing - (DateTimeOffset.UtcNow - previous);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, lifetime.Token);
                if (work.Cancellation.IsCancellationRequested) { Remove(work); work.Cancellation.Dispose(); continue; }
                previous = DateTimeOffset.UtcNow;
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                active[work.Version] = completion.Task; _ = RunAsync(work, completion);
            }
        }
        catch (OperationCanceledException) { }
    }
    private async Task RunAsync(Work work, TaskCompletionSource completion)
    {
        var retry = false; State(work, InstanceState.Starting);
        try
        {
            var pid = await dispatcher.DispatchAsync(work.Request, work.Cancellation.Token);
            work.Cancellation.Token.ThrowIfCancellationRequested();
            State(work, pid.HasValue ? InstanceState.Running : InstanceState.AwaitingProcess, pid, pid.HasValue ? null : "Launch sent; process identity could not be confirmed. Link its PID manually.");
            if (pid.HasValue) runningSince[work.Request.AccountId] = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException) when (work.Cancellation.IsCancellationRequested) { State(work, InstanceState.Exited, detail: "Launch cancelled."); }
        catch (OperationCanceledException) { State(work, InstanceState.Failed, detail: "Roblox timed out."); retry = true; }
        catch (RobloxLaunchException ex) { State(work, InstanceState.Failed, detail: ex.Message); retry = ex.Retryable; }
        catch (Exception) { State(work, InstanceState.Failed, detail: "Launch failed. Check Roblox and the connection."); retry = true; }
        finally
        {
            Remove(work); work.Cancellation.Dispose();
            if (retry && Current(work)) Retry(work.Request, work.Attempt + 1);
            active.TryRemove(work.Version, out _); completion.TrySetResult();
        }
    }
    private void Retry(LaunchRequest request, int attempt)
    {
        if (lifetime.IsCancellationRequested || policy?.AutoRejoin != true || stopped.ContainsKey(request.AccountId) || attempt > Math.Clamp(policy.MaxRejoinAttempts, 1, 10)) return;
        Work work; try { work = NewWork(request, attempt); } catch (RobloxLaunchException) { return; }
        var seconds = Math.Min(300, Math.Max(0.01, policy.RejoinDelaySeconds) * Math.Pow(2, attempt - 1));
        State(work, InstanceState.Backoff, detail: $"Rejoining in {Math.Ceiling(seconds)} seconds (attempt {attempt}).");
        _ = QueueRetryAsync(work, TimeSpan.FromSeconds(seconds));
    }
    private async Task QueueRetryAsync(Work work, TimeSpan delay)
    {
        var queued = false;
        try
        {
            await Task.Delay(delay, work.Cancellation.Token);
            if (policy?.AutoRejoin != true || work.Attempt > Math.Clamp(policy.MaxRejoinAttempts,1,10) || !Current(work)) { State(work, InstanceState.Exited); return; }
            State(work, InstanceState.Queued); await queue.Writer.WriteAsync(work, work.Cancellation.Token); queued = true;
        }
        catch (OperationCanceledException) { State(work, InstanceState.Exited, detail: "Rejoin cancelled."); }
        catch (ChannelClosedException) { }
        finally { if (!queued) { Remove(work); work.Cancellation.Dispose(); } }
    }
    private void RefreshProcesses()
    {
        foreach (var item in states.Values)
        {
            if (item.State == InstanceState.Running && tracker.GetLiveProcessId(item.AccountId) is null)
            {
                if (states.TryUpdate(item.AccountId, item with { State = InstanceState.Exited, ProcessId = null }, item) && targets.TryGetValue(item.AccountId, out var request)) Retry(request, item.RejoinAttempts + 1);
            }
            else if (item.State == InstanceState.Running && item.RejoinAttempts > 0 && runningSince.TryGetValue(item.AccountId, out var since) && DateTimeOffset.UtcNow - since > TimeSpan.FromMinutes(1)) states.TryUpdate(item.AccountId, item with { RejoinAttempts = 0 }, item);
            else if (item.State == InstanceState.AwaitingProcess && tracker.GetLiveProcessId(item.AccountId) is int pid)
            {
                states.TryUpdate(item.AccountId, item with { State = InstanceState.Running, ProcessId = pid, Detail = null }, item); runningSince[item.AccountId] = DateTimeOffset.UtcNow;
            }
        }
    }
    private async Task WatchAsync()
    {
        try { while (!lifetime.IsCancellationRequested) { await Task.Delay(watchInterval, lifetime.Token); RefreshProcesses(); } }
        catch (OperationCanceledException) { }
    }
    public void Dispose() { lifetime.Cancel(); queue.Writer.TryComplete(); }
    public async ValueTask DisposeAsync()
    {
        Dispose(); try { await Task.WhenAll(scheduler, watcher); await Task.WhenAll(active.Values); } catch (OperationCanceledException) { }
        foreach (var work in pending.Values) State(work, InstanceState.Exited, detail: "Launch cancelled."); pending.Clear();
    }
}
