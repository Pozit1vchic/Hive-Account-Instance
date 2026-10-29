using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using Hive.Core;
using Microsoft.Win32;
namespace Hive.Infrastructure;

public sealed class RobloxProcessLauncher(IRobloxLaunchBroker broker, IAccountActivityTracker tracker, IGameWindowPreferences? preferences = null) : ILaunchDispatcher
{
    public async Task<int?> DispatchAsync(LaunchRequest request, CancellationToken ct = default)
    {
        using (var protocol = Registry.ClassesRoot.OpenSubKey(@"roblox-player\shell\open\command"))
        {
            if (protocol is null)
                throw new RobloxLaunchException("Install Roblox Player before launching a game.");
        }
        var spec = await broker.CreateLaunchAsync(request, ct);
        // Authentication tickets are deliberately never logged, copied to the clipboard, or shown in UI.
        using var bootstrap = Process.Start(new ProcessStartInfo(spec.ProtocolUri) { UseShellExecute = true });
        var deadline = DateTimeOffset.UtcNow.AddSeconds(25);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var pid = await Task.Run(() => FindProcess(spec.BrowserTrackerId), ct);
            if (pid.HasValue)
            {
                try
                {
                    tracker.Attach(request.AccountId, pid.Value);
                    if (preferences?.CompactGameWindows != false)
                        _ = RobloxWindowSizer.ResizeWhenReadyAsync(pid.Value, ct);
                    return pid;
                }
                catch (ProcessAssignmentException) { }
            }
            await Task.Delay(500, ct);
        }
        return null;
    }
    public static bool MatchesTracker(string command, string trackerId)
    {
        if (trackerId.Length is < 1 or > 24 || trackerId.Any(c => !char.IsAsciiDigit(c)))
            return false;
        var escaped = Regex.Escape(trackerId);
        return Regex.IsMatch(command, @"(?:^|\s)-b\s+[""]?" + escaped + @"(?:[""]?(?:\s|$))")
            || Regex.IsMatch(command, @"(?:[+])browsertrackerid:" + escaped + @"(?=[+""\s]|$)", RegexOptions.IgnoreCase);
    }
    private static int? FindProcess(string trackerId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\cimv2"), new ObjectQuery("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='RobloxPlayerBeta.exe'"), new System.Management.EnumerationOptions { ReturnImmediately = false, Rewindable = false, Timeout = TimeSpan.FromSeconds(2) });
            using var results = searcher.Get();
            foreach (ManagementObject result in results)
            {
                using (result)
                {
                    if (result["CommandLine"] is string command && MatchesTracker(command, trackerId))
                        return checked((int)(uint)result["ProcessId"]);
                }
            }
        }
        catch (ManagementException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }
}
