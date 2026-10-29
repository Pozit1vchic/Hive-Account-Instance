using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows.Threading;
using Hive.Core;
using Hive.Roblox;

namespace Hive.App;

// Local, current-Windows-user control; deliberately does not expose secrets or a network listener.
internal sealed class LocalControlServer(AccountsViewModel model, IInstanceManager instances, Dispatcher dispatcher, string pipeName = "Hive.Control.v1") : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private Task? worker;
    internal void Start() => worker = ListenAsync();
    private async Task ListenAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var buffer = new char[8193]; var length = 0;
                while (length < buffer.Length)
                {
                    var read = await reader.ReadAsync(buffer.AsMemory(length, 1), timeout.Token);
                    if (read == 0 || buffer[length] == '\n') break;
                    length += read;
                }
                var line = new string(buffer, 0, length).TrimEnd('\r');
                string result;
                if (line is null || line.Length > 8192) result = JsonSerializer.Serialize(new { error = "Invalid request." });
                else
                {
                    try
                    {
                        using var request = JsonDocument.Parse(line);
                        result = await (await dispatcher.InvokeAsync(() => HandleAsync(request.RootElement.Clone(), timeout.Token)));
                    }
                    catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException) { result = JsonSerializer.Serialize(new { error = "Invalid request or launch target." }); }
                }
                await writer.WriteLineAsync(result.AsMemory(), timeout.Token);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException) { if (stop.IsCancellationRequested) break; await Task.Delay(250).ConfigureAwait(false); }
        }
    }
    private async Task<string> HandleAsync(JsonElement request, CancellationToken ct)
    {
        if (!request.TryGetProperty("action", out var action)) return JsonSerializer.Serialize(new { error = "Missing action." });
        if (action.GetString() == "accounts")
            return JsonSerializer.Serialize(new { accounts = model.ControlAccounts() });
        if (action.GetString() == "launch")
        {
            var id = request.GetProperty("accountId").GetString() ?? "";
            if (!model.ControlAccounts().Any(a => a.Id == id)) return JsonSerializer.Serialize(new { error = "Account not found." });
            var target = request.GetProperty("placeId").GetInt64();
            var job = request.TryGetProperty("jobId", out var j) ? j.GetString() ?? "" : "";
            try { await instances.EnqueueAsync(LaunchTarget.Parse(id, LaunchKind.Place, target.ToString(System.Globalization.CultureInfo.InvariantCulture), job), ct); }
            catch (Exception ex) when (ex is RobloxLaunchException or FormatException) { return JsonSerializer.Serialize(new { error = ex.Message }); }
            return JsonSerializer.Serialize(new { queued = true, accountId = id });
        }
        return JsonSerializer.Serialize(new { error = "Unknown action." });
    }
    public void Dispose() { stop.Cancel(); }
}
