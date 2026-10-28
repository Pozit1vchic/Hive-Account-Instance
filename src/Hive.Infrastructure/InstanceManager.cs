using System.Collections.Concurrent;
using System.Threading.Channels;
using Hive.Core;
namespace Hive.Infrastructure;

public sealed class InstanceManager : IInstanceManager, IAsyncDisposable, IDisposable
{
    private readonly ILaunchDispatcher dispatcher;
    private readonly IAccountActivityTracker tracker;
    private readonly Channel<LaunchRequest> queue = Channel.CreateBounded<LaunchRequest>(50);
    private readonly ConcurrentDictionary<string, GameInstance> states = [];
    private readonly ConcurrentDictionary<string, byte> pending = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task worker;
    private readonly TimeSpan spacing;
    public InstanceManager(ILaunchDispatcher dispatcher, IAccountActivityTracker tracker, TimeSpan? launchSpacing = null)
    {
        this.dispatcher = dispatcher;
        this.tracker = tracker;
        spacing = launchSpacing ?? TimeSpan.FromSeconds(3);
        if (spacing < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(launchSpacing));
        worker = RunAsync();
    }
    public IReadOnlyList<GameInstance> Instances
    {
        get
        {
            foreach (var item in states.Values)
            {
                if (item.State == InstanceState.Running && tracker.GetLiveProcessId(item.AccountId) is null)
                    states[item.AccountId] = item with
                    {
                        State = InstanceState.Exited,
                        ProcessId = null
                    };
                else if (item.State == InstanceState.AwaitingProcess && tracker.GetLiveProcessId(item.AccountId) is int pid)
                    states[item.AccountId] = item with
                    {
                        State = InstanceState.Running,
                        ProcessId = pid
                    };
            }
            return states.Values.ToList();
        }
    }
    public Task EnqueueAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (lifetime.IsCancellationRequested)
            throw new InvalidOperationException("Launch queue is closed.");
        if (tracker.GetLiveProcessId(request.AccountId).HasValue)
            throw new RobloxLaunchException("This account already has a linked Roblox process.");
        if (!pending.TryAdd(request.AccountId, 0))
            throw new RobloxLaunchException("This account is already queued.");
        states[request.AccountId] = new(request.AccountId, null, InstanceState.Queued, 0);
        if (!queue.Writer.TryWrite(request))
        {
            pending.TryRemove(request.AccountId, out _);
            states[request.AccountId] = new(request.AccountId, null, InstanceState.Failed, 0, "Launch queue is full.");
            throw new RobloxLaunchException("Launch queue is full. Wait for a pending launch.");
        }
        return Task.CompletedTask;
    }
    private async Task RunAsync()
    {
        try
        {
            await foreach (var request in queue.Reader.ReadAllAsync(lifetime.Token))
            {
                states[request.AccountId] = new(request.AccountId, null, InstanceState.Starting, 0);
                try
                {
                    var pid = await dispatcher.DispatchAsync(request, lifetime.Token);
                    states[request.AccountId] = new(request.AccountId, pid, pid.HasValue ? InstanceState.Running : InstanceState.AwaitingProcess, 0, pid.HasValue ? null : "Launch sent; process identity could not be confirmed. Link its PID manually.");
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { states[request.AccountId] = new(request.AccountId, null, InstanceState.Exited, 0, "Launch cancelled."); }
                catch (RobloxLaunchException ex) { states[request.AccountId] = new(request.AccountId, null, InstanceState.Failed, 0, ex.Message); }
                catch (Exception) { states[request.AccountId] = new(request.AccountId, null, InstanceState.Failed, 0, "Launch failed. Check the Roblox installation, session and connection."); }
                finally { pending.TryRemove(request.AccountId, out _); }
                await Task.Delay(spacing, lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
    }
    public void Dispose()
    {
        lifetime.Cancel();
        queue.Writer.TryComplete();
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        queue.Writer.TryComplete();
        try
        {
            await worker;
        }
        catch (OperationCanceledException) { }
        foreach (var id in pending.Keys)
        {
            states[id] = new(id, null, InstanceState.Exited, 0, "Launch cancelled.");
        }
        pending.Clear();
    }
}
