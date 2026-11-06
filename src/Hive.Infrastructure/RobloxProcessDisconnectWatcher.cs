using System.Diagnostics;
namespace Hive.Infrastructure;

public interface IRobloxProcessDisconnectWatcher
{
    Task WatchAsync(int processId, Func<bool> stillCurrent, CancellationToken ct);
}

public sealed class RobloxProcessDisconnectWatcher : IRobloxProcessDisconnectWatcher
{
    private static readonly SemaphoreSlim DiscoveryGate = new(2, 2);
    public async Task WatchAsync(int processId, Func<bool> stillCurrent, CancellationToken ct)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.ProcessName.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase)) return;
            var started = process.StartTime.ToUniversalTime();
            var locator = new RobloxLogLocator();
            string? path = null;
            while (path is null && stillCurrent())
            {
                ct.ThrowIfCancellationRequested();
                await DiscoveryGate.WaitAsync(ct);
                try { path = await Task.Run(() => locator.Find(processId, started)); }
                finally { DiscoveryGate.Release(); }
                if (path is null) await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
            if (path is null) return;
            await new RobloxDisconnectMonitor().WaitForDisconnectAsync(path, ct);
            ct.ThrowIfCancellationRequested();
            if (!stillCurrent() || process.HasExited || process.StartTime.ToUniversalTime() != started || !process.ProcessName.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase)) return;
            // A normal window close lets the existing process-exit watcher schedule rejoin.
            // Do not force-kill a client that does not accept the close request.
            process.CloseMainWindow();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
    }
}
