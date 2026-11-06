using System.Text;
namespace Hive.Infrastructure;

public sealed class RobloxConnectionState
{
    private bool connected;
    private DateTimeOffset? disconnectedAt;
    public void Observe(string line, DateTimeOffset now)
    {
        if (line.Contains("[FLog::Output] ! Joining game '", StringComparison.Ordinal) && line.Contains("' place ", StringComparison.Ordinal)) { connected = true; disconnectedAt = null; }
        else if (connected && line.Contains("[FLog::Network] Sending disconnect with reason: ", StringComparison.Ordinal)) { connected = false; disconnectedAt = now; }
    }
    public bool IsDisconnected(DateTimeOffset now, TimeSpan grace) => disconnectedAt is { } since && now - since >= grace;
}

public sealed class RobloxDisconnectMonitor(TimeSpan? interval = null, TimeSpan? grace = null)
{
    private readonly TimeSpan pollInterval = interval ?? TimeSpan.FromSeconds(1);
    private readonly TimeSpan disconnectGrace = grace ?? TimeSpan.FromSeconds(60);
    public async Task WaitForDisconnectAsync(string logPath, CancellationToken ct = default)
    {
        var state = new RobloxConnectionState();
        using var file = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        if (file.Length > 262144) file.Seek(-262144, SeekOrigin.End);
        var buffer = new byte[4096]; var line = new StringBuilder();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (file.Length < file.Position) { file.Position = 0; line.Clear(); state = new(); }
            var bytes = await file.ReadAsync(buffer, ct);
            if (bytes == 0)
            {
                if (state.IsDisconnected(DateTimeOffset.UtcNow, disconnectGrace)) return;
                await Task.Delay(pollInterval, ct); continue;
            }
            foreach (var b in buffer.AsSpan(0, bytes))
            {
                if (b == '\n') { state.Observe(line.ToString(), DateTimeOffset.UtcNow); line.Clear(); }
                else if (line.Length < 8192) line.Append((char)b);
            }
        }
    }
}
