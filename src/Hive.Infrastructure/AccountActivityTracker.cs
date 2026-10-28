using System.Diagnostics;
using System.Collections.Concurrent;
using Hive.Core;
namespace Hive.Infrastructure;

public sealed record ProcessSnapshot(string Name, DateTime StartedUtc);
// Only the launcher or an explicit user assignment can identify the account behind a process.
public sealed class AccountActivityTracker(Func<int, ProcessSnapshot?>? inspect = null) : IAccountActivityTracker
{
    private readonly object sync = new();
    private readonly ConcurrentDictionary<string, (int Pid, DateTime Started)> links = [];
    private ProcessSnapshot? Inspect(int pid)
    {
        if (inspect is not null)
            return inspect(pid);
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited ? null : new(process.ProcessName, process.StartTime.ToUniversalTime());
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }
    public void Attach(string accountId, int processId)
    {
        var process = Inspect(processId);
        if (process is null || !process.Name.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase))
            throw new ProcessAssignmentException("Select the PID of a running RobloxPlayerBeta process.");
        lock (sync)
        {
            if (links.Any(x => x.Key != accountId && x.Value.Pid == processId))
                throw new ProcessAssignmentException("This PID is already linked to another account.");
            links[accountId] = (processId, process.StartedUtc);
        }
    }
    public int? GetLiveProcessId(string accountId)
    {
        if (!links.TryGetValue(accountId, out var link))
            return null;
        var process = Inspect(link.Pid);
        if (process is not null && process.Name.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase) && process.StartedUtc == link.Started)
            return link.Pid;
        links.TryRemove(accountId, out _);
        return null;
    }
    public void Detach(string accountId) => links.TryRemove(accountId, out _);
}
