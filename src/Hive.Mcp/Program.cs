using System.ComponentModel;
using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<HiveTools>();
await builder.Build().RunAsync();

[McpServerToolType]
public sealed class HiveTools
{
    [McpServerTool, Description("List Hive accounts and their linked running process states. Hive must be open on this Windows account. No passwords or cookies are returned.")]
    public static Task<string> ListAccounts(CancellationToken ct) => SendAsync(new { action = "accounts" }, ct);

    [McpServerTool, Description("Queue a Hive account to join a Roblox place. Use the exact account ID from ListAccounts; an optional server Job ID must be a GUID. Returns queued status, not proof that the game joined.")]
    public static Task<string> LaunchAccount([Description("Account ID returned by ListAccounts")] string accountId, [Description("Positive Roblox Place ID")] long placeId, [Description("Optional server Job ID")] string? jobId = null, CancellationToken ct = default)
        => SendAsync(new { action = "launch", accountId, placeId, jobId }, ct);

    private static async Task<string> SendAsync(object request, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await using var pipe = new NamedPipeClientStream(".", "Hive.Control.v1", PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token);
            return await reader.ReadLineAsync(timeout.Token) ?? "Hive closed the connection.";
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException) { return "Hive is unavailable. Open Hive under the same Windows user and try again."; }
    }
}

