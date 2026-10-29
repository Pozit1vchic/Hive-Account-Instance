using System.Diagnostics;
using Hive.Core;
namespace Hive.Infrastructure;

public sealed class MultiInstanceCoordinator(Func<bool>? clientsRunning = null, string mutexName = "ROBLOX_singletonMutex", string eventName = "ROBLOX_singletonEvent") : IMultiInstanceCoordinator
{
    private readonly object sync = new();
    private Thread? owner;
    private ManualResetEventSlim? release;
    public void Prepare()
    {
        lock (sync)
        {
            if (owner?.IsAlive == true) return;
            var running = clientsRunning?.Invoke() ?? HasClients();
            if (running) throw new RobloxLaunchException("Close existing Roblox clients before enabling multi-instance.");
            release = new ManualResetEventSlim();
            var signal = release;
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            owner = new Thread(() =>
            {
                Mutex? singleton = null; Mutex? eventGuard = null; var acquired = false;
                try
                {
                    singleton = new Mutex(false, mutexName);
                    try { acquired = singleton.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new RobloxLaunchException("Roblox singleton is already owned. Close Roblox and retry.");
                    eventGuard = new Mutex(false, eventName, out var created);
                    if (!created) throw new RobloxLaunchException("Roblox singleton event is already in use. Close Roblox and retry.");
                    ready.TrySetResult(); signal.Wait();
                }
                catch (Exception) { ready.TrySetException(new RobloxLaunchException("Multi-instance could not reserve the Roblox singleton objects. Close Roblox and retry.")); }
                finally { eventGuard?.Dispose(); if (acquired) singleton?.ReleaseMutex(); singleton?.Dispose(); }
            }) { IsBackground = true, Name = "Hive multi-instance owner" };
            owner.Start();
            try { ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
            catch { signal.Set(); owner.Join(1000); if (!owner.IsAlive) { signal.Dispose(); release = null; owner = null; } throw; }
        }
    }
    private static bool HasClients()
    {
        var processes = Process.GetProcessesByName("RobloxPlayerBeta");
        try { return processes.Length > 0; } finally { foreach (var process in processes) process.Dispose(); }
    }
    public void Dispose()
    {
        lock (sync) { release?.Set(); owner?.Join(2000); if (owner?.IsAlive != true) { release?.Dispose(); release = null; owner = null; } }
    }
}
