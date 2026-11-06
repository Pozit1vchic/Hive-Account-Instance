using System.IO;
using Hive.Infrastructure;

internal static class DisconnectChecks
{
    public static async Task RunAsync(string folder)
    {
        var now = DateTimeOffset.UtcNow;
        const string join = "[FLog::Output] ! Joining game 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' place 123 at 127.0.0.1";
        const string disconnect = "[FLog::Network] Sending disconnect with reason: 1";
        var state = new RobloxConnectionState();
        state.Observe(disconnect, now);
        if (state.IsDisconnected(now.AddMinutes(3), TimeSpan.FromSeconds(60))) throw new Exception("Unconnected process marked disconnected");
        state.Observe(join, now); state.Observe(disconnect, now);
        if (state.IsDisconnected(now.AddSeconds(59), TimeSpan.FromSeconds(60)) || !state.IsDisconnected(now.AddSeconds(60), TimeSpan.FromSeconds(60))) throw new Exception("Disconnect grace failed");
        state.Observe(join, now.AddSeconds(61));
        if (state.IsDisconnected(now.AddMinutes(3), TimeSpan.FromSeconds(60))) throw new Exception("Reconnect did not cancel disconnect");
        var path = Path.Combine(folder, "disconnect-fixture.log");
        await File.WriteAllTextAsync(path, join + "\n");
        var monitor = new RobloxDisconnectMonitor(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(50));
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var pending = monitor.WaitForDisconnectAsync(path, cancel.Token);
        await Task.Delay(50);
        await File.AppendAllTextAsync(path, disconnect[..20]);
        await Task.Delay(70);
        if (pending.IsCompleted) throw new Exception("Incomplete log line caused a disconnect");
        await File.AppendAllTextAsync(path, disconnect[20..] + "\n");
        await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Console.WriteLine("PASS: disconnect grace, reconnect, bounded partial log lines and log tailing.");
    }
}
